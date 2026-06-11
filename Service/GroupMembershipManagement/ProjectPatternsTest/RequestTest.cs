// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ProjectPatternsTest
{
    [TestClass]
    public class RequestTest
    {
        [TestMethod]
        public void RequestClassesHaveRequiredProperties()
        {
            // Get the base directory and navigate to the MembershipAggregator project
            var baseDirectory = GetProjectBaseDirectory();

            var listOfFunctionNames = new List<string>
            {
                "MembershipAggregator"
            };

            foreach(var functionName in listOfFunctionNames)
            {
                var functionPath = Path.Combine(baseDirectory, "Hosts", functionName);

                if (!Directory.Exists(functionPath))
                {
                    Assert.Fail($"{functionName} directory not found at: {functionPath}");
                }

                // Find all assemblies in the MembershipAggregator directory
                var assemblyFiles = Directory.GetFiles(functionPath, "*.dll", SearchOption.AllDirectories)
                                            .Where(f => !Path.GetDirectoryName(f)!.EndsWith("bin") &&
                                                !Path.GetFullPath(f).Contains("obj") &&
                                                !Path.GetFullPath(f).Contains("ref") &&
                                                Path.GetFullPath(f).Contains("MembershipAggregator\\Function\\bin") &&
                                                (Path.GetFileName(f).Contains("MembershipAggregator") ||
                                                Path.GetFileName(f).StartsWith("Services.")))
                                            .ToList();

                var violations = new List<string>();

                foreach (var assemblyFile in assemblyFiles)
                {
                    List<Type> allClasses = new List<Type>();
                    try
                    {
                        var assembly = Assembly.LoadFrom(assemblyFile);
                        allClasses = assembly.GetTypes().ToList();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        allClasses = ex.Types.Where(t => t != null).ToList<Type>();
                    }
                    catch (Exception ex)
                    {
                        // Skip assemblies that can't be loaded (common with native or incompatible assemblies)
                        Console.WriteLine($"Skipping assembly {assemblyFile}: {ex.Message}");
                    }

                    var requestClasses = allClasses
                        .Where(t => t.IsClass &&
                                !t.IsAbstract &&
                                t.Name.EndsWith("Request", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var requestClass in requestClasses)
                    {
                        var className = requestClass.Name;
                        var properties = requestClass.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                        foreach (var property in properties)
                        {
                            // Check if property has Required attribute
                            var hasRequiredAttribute = property.GetCustomAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>() != null;

                            // Check if property has nullable annotation (C# 8+ nullable reference types)
                            var hasNullableAnnotation = HasNullableAnnotation(property);

                            // Property should be required if it's not nullable
                            if (!hasRequiredAttribute && !hasNullableAnnotation)
                            {
                                violations.Add($"{requestClass.FullName}.{property.Name} is not marked as required and is not nullable");
                            }
                        }
                    }
                }

                if (violations.Any())
                {
                    var message = $"Found {violations.Count} Request class properties that are not properly marked as required:\n" +
                                 string.Join("\n", violations);
                    Assert.Fail(message);
                }
            }
        }

        private static string GetProjectBaseDirectory()
        {
            // Get the directory where this test assembly is located
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var directory = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation));
            
            // Navigate up to find the Service directory
            while (directory != null && directory.Name != "GroupMembershipManagement")
            {
                directory = directory.Parent;
            }
            
            if (directory == null)
            {
                throw new InvalidOperationException("Could not find Service directory in the path hierarchy");
            }

            return directory.FullName;
        }

        private static bool IsNullableType(Type type)
        {
            // Check for Nullable<T> (value types)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return true;
            }
            
            // Reference types are nullable by default in older C# versions
            // In C# 8+ with nullable reference types enabled, we need to check the nullable annotation
            return !type.IsValueType;
        }

        private static bool HasNullableAnnotation(PropertyInfo property)
        {
            // Check for nullable annotation in C# 8+ nullable reference types
            var nullableAttribute = property.GetCustomAttribute(typeof(System.Runtime.CompilerServices.NullableAttribute));
            if (nullableAttribute != null)
            {
                // If NullableAttribute exists, check its flag value
                var flags = (byte[])nullableAttribute.GetType().GetField("NullableFlags")?.GetValue(nullableAttribute);
                return flags != null && flags.Length > 0 && flags[0] == 2; // 2 means nullable
            }

            // Check nullable context at the property level
            var nullableContextAttribute = property.DeclaringType?.GetCustomAttribute(typeof(System.Runtime.CompilerServices.NullableContextAttribute));
            if (nullableContextAttribute != null)
            {
                var flag = (byte)nullableContextAttribute.GetType().GetField("Flag")?.GetValue(nullableContextAttribute);
                return flag == 2; // 2 means nullable context enabled
            }

            return false;
        }
    }
}
