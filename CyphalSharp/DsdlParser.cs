using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CyphalSharp
{
    /// <summary>
    /// Provides methods for parsing Cyphal DSDL files and directories.
    /// </summary>
    public static class DsdlParser
    {
        /// <summary>
        /// Parses all DSDL files within a specified directory and its subdirectories.
        /// </summary>
        /// <param name="rootPath">The root directory path containing DSDL files.</param>
        /// <returns>A dictionary where the key is the file name and the value is the parsed <see cref="Cyphal"/> object.</returns>
        public static Dictionary<string, Cyphal> ParseDirectory(string rootPath)
        {
            var dsdls = new Dictionary<string, Cyphal>();
            var dsdlFiles = Directory.GetFiles(rootPath, "*.dsdl", SearchOption.AllDirectories);

            foreach (var file in dsdlFiles)
            {
                var relativePath = GetRelativePath(rootPath, file);
                var fileName = Path.GetFileName(file);
                
                // Extract TypeName, Major, Minor from filename: <TypeName>.<major>.<minor>.dsdl
                var match = Regex.Match(fileName, @"^(\w+)\.(\d+)\.(\d+)\.dsdl$");
                if (!match.Success) continue;

                var typeName = match.Groups[1].Value;
                var major = int.Parse(match.Groups[2].Value);
                var minor = int.Parse(match.Groups[3].Value);

                var namespacePath = Path.GetDirectoryName(relativePath).Replace(Path.DirectorySeparatorChar, '.');
                var fullTypeName = string.IsNullOrEmpty(namespacePath) ? typeName : $"{namespacePath}.{typeName}";

                var dsdl = ParseFile(file, fullTypeName, major, minor);
                dsdls[fileName] = dsdl;
            }

            return dsdls;
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            var fullRelativeTo = Path.GetFullPath(relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString()) ? relativeTo : relativeTo + Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            var uriRelativeTo = new Uri(fullRelativeTo);
            var uriPath = new Uri(fullPath);
            var relativeUri = uriRelativeTo.MakeRelativeUri(uriPath);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Parses a single DSDL file.
        /// </summary>
        /// <param name="filePath">The path to the DSDL file.</param>
        /// <param name="fullTypeName">The full name of the type, including its namespace.</param>
        /// <param name="major">The major version number.</param>
        /// <param name="minor">The minor version number.</param>
        /// <param name="portIdOverride">Optional port ID override. If provided, this will be used instead of any ID found in the DSDL.</param>
        /// <returns>A <see cref="Cyphal"/> object representing the parsed DSDL file.</returns>
        public static Cyphal ParseFile(string filePath, string fullTypeName, int major, int minor, uint? portIdOverride = null)
        {
            var lines = File.ReadAllLines(filePath);
            var dsdl = new Cyphal();

            uint? discoveredPortId = null;

            var message = new Message
            {
                Name = fullTypeName,
                PortId = 0
            };

            bool parsingResponse = false;
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Handle comments and directives
                if (trimmedLine.StartsWith("#") || string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                if (trimmedLine.StartsWith("@"))
                {
                    if (trimmedLine == "@union")
                    {
                        message.IsUnion = true;
                    }
                    else if (trimmedLine.StartsWith("@__key__"))
                    {
                        var match = Regex.Match(trimmedLine, @"@__key__\s+(\d+)");
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out var portId))
                        {
                            discoveredPortId = portId;
                        }
                    }
                    else if (char.IsDigit(trimmedLine[1]))
                    {
                        if (uint.TryParse(trimmedLine.Substring(1), out var portId))
                        {
                            discoveredPortId = portId;
                        }
                    }
                    continue;
                }

                // Handle service separator
                if (trimmedLine == "---")
                {
                    message.IsServiceDefinition = true;
                    // In Cyphal/UDP, Service IDs use a different bit range, but for port registry we mark it.
                    message.PortId |= 0x8000; 
                    parsingResponse = true;
                    continue;
                }

                // Remove inline comments
                var content = trimmedLine.Split('#')[0].Trim();
                if (string.IsNullOrEmpty(content)) continue;
                
                // Handle fields/constants
                var parts = Regex.Split(content, @"\s+");
                if (parts.Length >= 2)
                {
                    var type = parts[0];
                    var name = parts[1];

                    // Check for constant: type name = value
                    if (parts.Length >= 4 && parts[2] == "=")
                    {
                        // Constants are skipped for field parsing but could be stored if Message had a Constants list
                        continue;
                    }

                    var field = new Field
                    {
                        Type = MapDsdlTypeToInternalType(type),
                        Name = name,
                        TagBody = content
                    };

                    if (parsingResponse)
                        message.ResponseFields.Add(field);
                    else
                        message.Fields.Add(field);
                }
            }

            // Post-process fields to set bit lengths and offsets
            int currentBitOffset = 0;
            foreach (var f in message.Fields)
            {
                f.SetDataType();
                f.SetBitLength();
                f.BitOffset = currentBitOffset;
                currentBitOffset += f.BitLength;
            }
            message.PayloadLength = (currentBitOffset + 7) / 8;

            // Process union fields
            if (message.IsUnion && message.Fields.Count > 0)
            {
                message.UnionTagFieldIndex = 0;
                var tagField = message.Fields[0];
                int tagBitLength = tagField.BitLength;

                for (int i = 1; i < message.Fields.Count; i++)
                {
                    message.Fields[i].IsUnionVariant = true;
                    message.Fields[i].UnionTagValue = i - 1;
                }

                var unionPayloadBits = 0;
                for (int i = 1; i < message.Fields.Count; i++)
                {
                    unionPayloadBits = Math.Max(unionPayloadBits, message.Fields[i].BitOffset + message.Fields[i].BitLength);
                }
                message.PayloadLength = (tagBitLength + unionPayloadBits + 7) / 8;
            }

            if (message.IsServiceDefinition)
            {
                currentBitOffset = 0;
                foreach (var f in message.ResponseFields)
                {
                    f.SetDataType();
                    f.SetBitLength();
                    f.BitOffset = currentBitOffset;
                    currentBitOffset += f.BitLength;
                }
                message.ResponsePayloadLength = (currentBitOffset + 7) / 8;
            }

            // Set port ID: override > discovered > hash fallback
            uint fallbackMask = message.IsServiceDefinition ? 0x1FFu : 0x1FFFu;
            message.PortId = portIdOverride ?? discoveredPortId ?? (uint)(fullTypeName.GetHashCode() & fallbackMask);
            if (message.IsServiceDefinition)
            {
                message.PortId |= 0x8000;
            }

            dsdl.Messages.Add(message);
            return dsdl;
        }

        private static uint CalculateFixedPortId(string fullTypeName)
        {
            // This is just a placeholder. Real Cyphal uses fixed port IDs for some types.
            // Or the ID is provided externally.
            return (uint)(fullTypeName.GetHashCode() & 0xFFFF);
        }

        private static string MapDsdlTypeToInternalType(string dsdlType)
        {
            // Extract array part if present: type[length] or type[<=length]
            var arrayMatch = Regex.Match(dsdlType, @"^([^\[]+)\[(<=)?(\d+)\]$");
            string baseType = dsdlType;
            string arraySuffix = "";

            if (arrayMatch.Success)
            {
                baseType = arrayMatch.Groups[1].Value;
                arraySuffix = "[" + arrayMatch.Groups[3].Value + "]";
            }

            string mappedType = baseType switch
            {
                "uint64" => "uint64_t",
                "uint32" => "uint32_t",
                "uint16" => "uint16_t",
                "uint8"  => "uint8_t",
                "int64"  => "int64_t",
                "int32"  => "int32_t",
                "int16"  => "int16_t",
                "int8"   => "int8_t",
                "float64" => "double",
                "float32" => "float",
                "float16" => "float16", // Placeholder
                "bool"    => "bool",
                var s when s.StartsWith("uint") => s, // Handle uintN
                var s when s.StartsWith("int") => s,  // Handle intN
                var s when s.StartsWith("void") => s, // Handle voidN
                _ => baseType
            };

            return mappedType + arraySuffix;
        }
    }
}
