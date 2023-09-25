using BMG.UI;
using Game.Interface;
using HarmonyLib;
using Home.Common;
using Mentions.UI;
using SalemModLoader;
using Server.Shared.Extensions;
using Server.Shared.Info;
using Server.Shared.Messages;
using Server.Shared.State;
using Server.Shared.State.Chat;
using Services;
using SML;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CommandCore
{
    public class CommandCore
    {
        public static List<Command> COMMANDS = new List<Command>();

        public CommandCore()
        {
            Assembly assembly = new StackTrace().GetFrame(1).GetMethod().ReflectedType.Assembly;
            Utils.ModLog("Loaded assembly");

            List<Type> commandTypes = Utils.GetTypesWithCustomAttribute<CommandDefinition>(assembly);
            Utils.ModLog("Loaded types");

            foreach (Type type in commandTypes)
            {
                COMMANDS.Add(BuildCommandFromType(type));
            }
        }

        private Command BuildCommandFromType(Type type)
        {
            CommandDefinition def = type.GetCustomAttribute<CommandDefinition>();
            CommandDescription desc = type.GetCustomAttribute<CommandDescription>();
            Utils.ModLog("Loaded definition");

            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

            Func<string[], bool> validator = (string[] args) => false;
            Action<string[]> executor = (string[] args) => { };

            methods.ForEach(m =>
            {
                if (m.Name == "Validate")
                {
                    try
                    {
                        validator = (Func<string[], bool>)Delegate.CreateDelegate(typeof(Func<string[], bool>), m);
                    } catch (Exception e)
                    {
                        Utils.ModLog("Improperly formatted validator: " + e.Message);
                    }
                    
                } else if (m.Name == "Execute")
                {
                    try
                    {
                        executor = (Action<string[]>)Delegate.CreateDelegate(typeof(Action<string[]>), m);
                    } catch (Exception e)
                    {
                        Utils.ModLog("Improperly formatted executor: " + e.Message);
                    }
                }
            });

            Utils.ModLog("building " + def.name + " command");

            return (desc != null) ? new Command(def.name, def.aliases, desc.description, desc.syntax, validator, executor) : new Command(def.name, def.aliases, validator, executor);
        }

        
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CommandDefinition : Attribute
    {

        public string name { get; }
        public string[] aliases { get; }

        public CommandDefinition(string name, params string[] aliases)
        {
            this.name = name;
            this.aliases = aliases;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CommandDescription : Attribute
    {

        public string description { get; }
        public string syntax { get; }

        public CommandDescription(string description, string syntax)
        {
            this.description = description;
            this.syntax = syntax;
        }
    }

    public class Command
    {

        public string name { get; }
        public string description { get; }
        public string syntax { get; }
        public bool hasDescription { get; }
        public string[] aliases { get; }
        private Func<string[], bool> validator;
        private Action<string[]> executor;

        public Command(string name, string[] aliases, Func<string[], bool> validator, Action<string[]> executor)
        {
            this.name = name;
            this.aliases = aliases;
            this.validator = validator;
            this.executor = executor;
            this.hasDescription = false;
        }

        public Command(string name, string[] aliases, string description, string syntax, Func<string[], bool> validator, Action<string[]> executor)
        {
            this.name = name;
            this.aliases = aliases;
            this.description = description;
            this.syntax = syntax;
            this.hasDescription = true;
            this.validator = validator;
            this.executor = executor;
        }

        public void Execute(string[] args)
        {
            executor.Invoke(args);
        }

        public bool Validate(string[] args)
        {
            return validator.Invoke(args);
        }
    }
    
    [HarmonyPatch(typeof(ChatInputController))]
    public class CommandController
    {
        [HarmonyPatch("SubmitChat")]
        [HarmonyPrefix]
        public static void Prefix(ChatInputController __instance)
        {
            __instance.chatInput.text = __instance.chatInput.text.Trim();
            string text = __instance.chatInput.text;

            if (text.StartsWith("/"))
            {
                string[] tokens = text.Split(' ');

                string name = tokens[0].Substring(1);
                string[] args = tokens.Skip(1).ToArray();

                Command foundCommand = null;

                if (CommandCore.COMMANDS.Any((Command command) =>
                {
                    if (command.name != name && !command.aliases.Contains(name)) return false;

                    if (!command.Validate(args)) return false;

                    foundCommand = command;

                    return true;
                }))
                {
                    if (foundCommand != null)
                    {
                        foundCommand.Execute(args);
                    }
                }

                if (foundCommand == null) Utils.AddFeedbackMsg("Unrecognized command '" + name + "'", true, "critical");

                __instance.chatInput.text = string.Empty;
            }
        }
    }
    
    [Mod.SalemMod]
    public class Main
    {

        public static void Start()
        {
            Utils.ModLog("ain't no way");

            new CommandCore();
        }
    }

    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "CommandCore";

        public const string PLUGIN_NAME = "Command Core";

        public const string PLUGIN_VERSION = "1.0.0";
    }

    [CommandDefinition("help", "h")]
    [CommandDescription("Lists all commands or gives detailed information on a specified command.", "/help [command name]")]
    public static class HelpCommand
    {
        public static bool Validate(string[] args) { 
            if (args.Length > 1) return false;

            return true;
        }

        public static void Execute(string[] args)
        {
            if (args.Length == 0)
            {
                string message = "Commands: " + CommandCore.COMMANDS.First().name;
                CommandCore.COMMANDS.Skip(1).ForEach((Command cmd) => { message += ", " + cmd.name; });
                
                message += "\n\nUse '/help [command name]' to get detailed information on one of the above commands.";
                Utils.AddFeedbackMsg(message, true, "info");
                return;
            }

            Command command = null;
            CommandCore.COMMANDS.Any((Command cmd) =>
            {
                if (cmd.name != args[0]) return false;
                command = cmd;
                return true;
                
            });

            if (command == null)
            {
                Utils.AddFeedbackMsg("Unrecognized command '" + args[0] + "'", true, "critical");
                return;
            }

            SendHelpMessage(command);
        }

        private static void SendHelpMessage(Command command)
        {
            if (command == null) return;

            string message = "Name: /" + command.name;

            if (command.aliases.Length > 0)
            {
                message += "\nAlias(es): /" + command.aliases.First();
                command.aliases.Skip(1).ForEach((string s) => { message += ", /" + s; });
            }
            

            if (!command.hasDescription)
            {
                message += "\n" + command.name + " does not have a defined description.";

            } else
            {
                message += "\nDescription: " + command.description;
                message += "\nSyntax: " + command.syntax;
            }

            Utils.AddFeedbackMsg(message, true, "info");
        }
    }

    public static class Utils
    {
        public static void ModLog(string message)
        {
            Console.WriteLine("[" + MyPluginInfo.PLUGIN_GUID + "] " + message);
        }

        public static void AddFeedbackMsg(string message, bool playSound = true, string feedbackMessageType = "normal")
        {
            MentionPanel mentionPanel = (MentionPanel)UnityEngine.Object.FindObjectOfType(typeof(MentionPanel));
            ChatLogClientFeedbackEntry chatLogEntry = new ChatLogClientFeedbackEntry(StringToFeedbackType(feedbackMessageType), mentionPanel.mentionsProvider.DecodeText(message));
            ChatLogMessage chatLogMessage = new ChatLogMessage();
            chatLogMessage.chatLogEntry = chatLogEntry;
            Service.Game.Sim.simulation.incomingChatMessage.ForceSet(chatLogMessage);
            if (playSound)
            {
                UnityEngine.Object.FindObjectOfType<UIController>().PlaySound("Audio/UI/Error", false);
            }
        }

        public static ClientFeedbackType StringToFeedbackType(string str)
        {
            ClientFeedbackType result;

            switch (str)
            {
                case "normal":
                    result = (ClientFeedbackType)0;
                    break;
                case "info":
                    result = (ClientFeedbackType)4;
                    break;
                case "warning":
                    result = (ClientFeedbackType)2;
                    break;
                case "critical":
                    result = (ClientFeedbackType)1;
                    break;
                default:
                    Console.WriteLine("Error: " + str + " is not a valid feedback type, defaulting to normal");
                    result = 0;
                    break;
            }
            return result;
        }

        public static List<MethodInfo> GetMethodsWithCustomAttribute<T>(Type type) {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public);
            List<MethodInfo> methodsWithCustomAttribute = new List<MethodInfo>();

            foreach(MethodInfo method in methods)
            {
                if (method.GetCustomAttributes(typeof(T), true).Length > 0)
                {
                    methodsWithCustomAttribute.Add(method);
                }
            } 

            return methodsWithCustomAttribute;
        }

        public static List<Type> GetTypesWithCustomAttribute<T>(Assembly assembly)
        {
            Type[] types = assembly.GetTypes();
            List<Type> typesWithCustomAttribute = new List<Type>();

            foreach (Type type in types)
            {
                if (type.GetCustomAttributes(typeof(T), true).Length > 0)
                {
                    typesWithCustomAttribute.Add(type);
                }
            }

            return typesWithCustomAttribute;
        }
    }
}