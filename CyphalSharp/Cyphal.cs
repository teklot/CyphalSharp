using CyphalSharp.Enums;
using CyphalSharp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyphalSharp
{
    /// <summary>
    /// The main entry point for the CyphalSharp library. Manages DSDL loading and message registration.
    /// </summary>
    public class Cyphal
    {
        /// <summary>
        /// Gets the list of messages defined in this DSDL instance.
        /// </summary>
        public List<Message> Messages { get; set; } = new List<Message>();

        private static bool _isInitialized = false;

        /// <summary>
        /// The global registry of message definitions, keyed by Port ID (Subject ID).
        /// </summary>
        public static Dictionary<uint, Message> RegisteredMessages { get; } = new Dictionary<uint, Message>();

        /// <summary>
        /// Resets the library's initialization state. Primarily used for testing.
        /// </summary>
        public static void Reset()
        {
            _isInitialized = false;
            RegisteredMessages.Clear();
        }

        /// <summary>
        /// Throws an exception if the Initialize method has not been called.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static void ThrowIfNotInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Cyphal.Initialize() must be called before using the library.");
            }
        }
        
        /// <summary>
        /// Initializes the message metadata and internal objects using the given DSDL type.
        /// </summary>
        /// <param name="dsdlType">The type of the DSDL to initialize.</param>
        /// <param name="portIds">Optional. A list of Port IDs (Subject IDs) to include for parsing. If empty, all messages from the DSDL are included.</param>
        public static void Initialize(DsdlType dsdlType, params uint[] portIds)
        {
            // For now we assume DSDLs are in a directory named 'DSDL'
            Initialize("DSDL", portIds);
        }

        /// <summary>
        /// Initializes the message metadata and internal objects using the given DSDL.
        /// </summary>
        /// <param name="dsdlPath">
        /// The path to the DSDL directory or a specific DSDL file. 
        /// </param>
        /// <param name="portIds">Optional. A list of Port IDs (Subject IDs) to include for parsing. If empty, all messages from the DSDL are included.</param>
        public static void Initialize(string dsdlPath = "DSDL", params uint[] portIds)
        {
            if (!Directory.Exists(dsdlPath) && !File.Exists(dsdlPath))
            {
                dsdlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DSDL");
                if (!Directory.Exists(dsdlPath) && !File.Exists(dsdlPath))
                {
                    dsdlPath = "DSDL";
                }
            }

            var dsdls = DsdlParser.ParseDirectory(dsdlPath);

            foreach (var (_, dsdlInstance) in dsdls)
            {
                foreach (var message in dsdlInstance.Messages)
                {
                    RegisteredMessages[message.PortId] = message;
                }
            }

            IncludeMessages(portIds);

            _isInitialized = true;
        }

        /// <summary>
        /// Include the Port IDs for parsing.
        /// </summary>
        /// <param name="portIds">An array of unsigned integers representing the Port IDs to be included.</param>
        public static void IncludeMessages(params uint[] portIds)
        {
            var invalidIds = portIds.Where(id => !RegisteredMessages.ContainsKey(id));

            if (invalidIds.Any())
            {
                throw new ArgumentException($"Invalid Port ID(s): {string.Join(", ", invalidIds)}.");
            }

            if (portIds.Length == 0)
            {
                foreach (var (_, message) in RegisteredMessages)
                {
                    message.Include();
                }
            }
            else
            {
                foreach (var portId in portIds)
                {
                    RegisteredMessages[portId].Include();
                }
            }
        }

        /// <summary>
        /// Exclude the Port IDs from parsing.
        /// </summary>
        /// <param name="portIds">An array of unsigned integers representing the Port IDs to be excluded.</param>
        public static void ExcludeMessages(params uint[] portIds)
        {
            var invalidIds = portIds.Where(id => !RegisteredMessages.ContainsKey(id));

            if (invalidIds.Any())
            {
                throw new ArgumentException($"Invalid Port ID(s): {string.Join(", ", invalidIds)}.");
            }

            foreach (var portId in portIds)
            {
                RegisteredMessages[portId].Exclude();
            }
        }
    }
}
