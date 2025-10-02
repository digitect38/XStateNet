using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace XStateNet.Configuration
{
    /// <summary>
    /// Advanced configuration management for XStateNet applications
    /// </summary>
    public class ConfigurationManager
    {
        private readonly Dictionary<string, object> _configurations = new();
        private readonly Dictionary<Type, ConfigurationSchema> _schemas = new();
        private readonly List<IConfigurationValidator> _validators = new();
        private readonly string _configurationDirectory;
        private readonly object _lock = new object();

        public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
        public event EventHandler<ConfigurationValidationEventArgs>? ValidationFailed;

        public ConfigurationManager(string? configurationDirectory = null)
        {
            _configurationDirectory = configurationDirectory ?? Path.Combine(Environment.CurrentDirectory, "config");
            Directory.CreateDirectory(_configurationDirectory);

            // Register built-in validators
            _validators.Add(new RangeValidator());
            _validators.Add(new RequiredValidator());
            _validators.Add(new FormatValidator());
        }

        /// <summary>
        /// Register a configuration type with schema validation
        /// </summary>
        public void RegisterConfiguration<T>() where T : class, new()
        {
            var type = typeof(T);
            var schema = GenerateSchema<T>();

            lock (_lock)
            {
                _schemas[type] = schema;
            }

            Console.WriteLine($"✅ Registered configuration type: {type.Name}");
        }

        /// <summary>
        /// Load configuration from file
        /// </summary>
        public T LoadConfiguration<T>(string? fileName = null) where T : class, new()
        {
            var type = typeof(T);
            fileName ??= $"{type.Name}.json";
            var filePath = Path.Combine(_configurationDirectory, fileName);

            T configuration;

            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    configuration = JsonSerializer.Deserialize<T>(json, GetJsonOptions()) ?? new T();
                    Console.WriteLine($"📁 Loaded configuration from: {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to load configuration from {fileName}: {ex.Message}");
                    Console.WriteLine("Using default configuration...");
                    configuration = new T();
                }
            }
            else
            {
                Console.WriteLine($"📄 Configuration file {fileName} not found, creating with defaults...");
                configuration = new T();
                SaveConfiguration(configuration, fileName);
            }

            // Validate configuration
            var validationResult = ValidateConfiguration(configuration);
            if (!validationResult.IsValid)
            {
                Console.WriteLine($"⚠️ Configuration validation failed for {type.Name}:");
                foreach (var error in validationResult.Errors)
                {
                    Console.WriteLine($"   - {error}");
                }

                OnValidationFailed(new ConfigurationValidationEventArgs
                {
                    ConfigurationType = type,
                    ValidationResult = validationResult
                });
            }

            lock (_lock)
            {
                _configurations[type.FullName!] = configuration;
            }

            return configuration;
        }

        /// <summary>
        /// Save configuration to file
        /// </summary>
        public void SaveConfiguration<T>(T configuration, string? fileName = null) where T : class
        {
            var type = typeof(T);
            fileName ??= $"{type.Name}.json";
            var filePath = Path.Combine(_configurationDirectory, fileName);

            // Validate before saving
            var validationResult = ValidateConfiguration(configuration);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Cannot save invalid configuration: {string.Join(", ", validationResult.Errors)}");
            }

            var json = JsonSerializer.Serialize(configuration, GetJsonOptions());
            File.WriteAllText(filePath, json);

            _configurations[type.FullName!] = configuration;

            Console.WriteLine($"💾 Saved configuration to: {fileName}");

            OnConfigurationChanged(new ConfigurationChangedEventArgs
            {
                ConfigurationType = type,
                Configuration = configuration,
                FileName = fileName
            });
        }

        /// <summary>
        /// Get current configuration
        /// </summary>
        public T GetConfiguration<T>() where T : class, new()
        {
            var type = typeof(T);
            if (_configurations.TryGetValue(type.FullName!, out var config))
            {
                return (T)config;
            }

            return LoadConfiguration<T>();
        }

        /// <summary>
        /// Update configuration with partial changes
        /// </summary>
        public void UpdateConfiguration<T>(Action<T> updateAction, string? fileName = null) where T : class, new()
        {
            var configuration = GetConfiguration<T>();
            updateAction(configuration);
            SaveConfiguration(configuration, fileName);
        }

        /// <summary>
        /// Create configuration template
        /// </summary>
        public void CreateTemplate<T>(string? fileName = null, bool includeDocumentation = true) where T : class, new()
        {
            var type = typeof(T);
            fileName ??= $"{type.Name}.template.json";
            var filePath = Path.Combine(_configurationDirectory, fileName);

            var template = new T();
            var templateDoc = new ConfigurationTemplate
            {
                ConfigurationType = type.Name,
                Version = "1.0",
                Description = GetTypeDescription<T>(),
                Schema = _schemas.TryGetValue(type, out var schema) ? schema : GenerateSchema<T>(),
                DefaultConfiguration = template,
                Documentation = includeDocumentation ? GenerateDocumentation<T>() : null
            };

            var json = JsonSerializer.Serialize(templateDoc, GetJsonOptions());
            File.WriteAllText(filePath, json);

            Console.WriteLine($"📋 Created configuration template: {fileName}");
        }

        /// <summary>
        /// Validate configuration against schema
        /// </summary>
        public ValidationResult ValidateConfiguration<T>(T configuration) where T : class
        {
            var result = new ValidationResult();
            var type = typeof(T);

            // Get schema for validation
            if (!_schemas.TryGetValue(type, out var schema))
            {
                schema = GenerateSchema<T>();
                _schemas[type] = schema;
            }

            // Run built-in validators
            foreach (var validator in _validators)
            {
                var errors = validator.Validate(configuration, schema);
                result.Errors.AddRange(errors);
            }

            // Run custom validation attributes
            var context = new ValidationContext(configuration);
            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            bool isValid = Validator.TryValidateObject(configuration, context, validationResults, true);

            if (!isValid)
            {
                foreach (var validationResult in validationResults)
                {
                    result.Errors.Add(validationResult.ErrorMessage ?? "Unknown validation error");
                }
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        /// <summary>
        /// Export all configurations
        /// </summary>
        public void ExportAllConfigurations(string exportPath)
        {
            var export = new ConfigurationExport
            {
                ExportDate = DateTime.UtcNow,
                Configurations = new Dictionary<string, object>()
            };

            foreach (var (typeName, config) in _configurations)
            {
                export.Configurations[typeName] = config;
            }

            var json = JsonSerializer.Serialize(export, GetJsonOptions());
            File.WriteAllText(exportPath, json);

            Console.WriteLine($"📦 Exported all configurations to: {exportPath}");
        }

        /// <summary>
        /// Import configurations from export
        /// </summary>
        public void ImportConfigurations(string importPath)
        {
            if (!File.Exists(importPath))
            {
                throw new FileNotFoundException($"Import file not found: {importPath}");
            }

            var json = File.ReadAllText(importPath);
            var import = JsonSerializer.Deserialize<ConfigurationExport>(json, GetJsonOptions());

            if (import?.Configurations == null)
            {
                throw new InvalidOperationException("Invalid import file format");
            }

            foreach (var (typeName, config) in import.Configurations)
            {
                _configurations[typeName] = config;
            }

            Console.WriteLine($"📥 Imported configurations from: {importPath}");
        }

        /// <summary>
        /// Generate configuration report
        /// </summary>
        public ConfigurationReport GenerateReport()
        {
            var report = new ConfigurationReport
            {
                GeneratedAt = DateTime.UtcNow,
                ConfigurationDirectory = _configurationDirectory,
                RegisteredTypes = _schemas.Count,
                LoadedConfigurations = _configurations.Count,
                ConfigurationDetails = new List<ConfigurationDetail>()
            };

            // Create snapshot to avoid collection modified exception
            KeyValuePair<Type, ConfigurationSchema>[] schemasSnapshot;
            lock (_lock)
            {
                schemasSnapshot = _schemas.ToArray();
            }

            foreach (var (type, schema) in schemasSnapshot)
            {
                var typeName = type.FullName!;
                bool hasConfiguration;
                object? config = null;

                lock (_lock)
                {
                    hasConfiguration = _configurations.ContainsKey(typeName);
                    if (hasConfiguration)
                    {
                        config = _configurations[typeName];
                    }
                }

                var fileName = $"{type.Name}.json";
                var filePath = Path.Combine(_configurationDirectory, fileName);

                var detail = new ConfigurationDetail
                {
                    TypeName = type.Name,
                    FullTypeName = typeName,
                    FileName = fileName,
                    FileExists = File.Exists(filePath),
                    IsLoaded = hasConfiguration,
                    Schema = schema,
                    ValidationStatus = hasConfiguration && config != null ? ValidateConfiguration(config) : new ValidationResult { IsValid = false, Errors = { "Not loaded" } }
                };

                if (detail.FileExists)
                {
                    var fileInfo = new FileInfo(filePath);
                    detail.LastModified = fileInfo.LastWriteTime;
                    detail.FileSize = fileInfo.Length;
                }

                report.ConfigurationDetails.Add(detail);
            }

            return report;
        }

        /// <summary>
        /// Watch for configuration file changes
        /// </summary>
        public IDisposable WatchForChanges()
        {
            var watcher = new FileSystemWatcher(_configurationDirectory)
            {
                Filter = "*.json",
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;

            Console.WriteLine($"👁️ Watching for configuration changes in: {_configurationDirectory}");

            return watcher;
        }

        /// <summary>
        /// Add custom validator
        /// </summary>
        public void AddValidator(IConfigurationValidator validator)
        {
            _validators.Add(validator);
        }

        /// <summary>
        /// Interactive configuration editor
        /// </summary>
        public async Task StartInteractiveEditor()
        {
            Console.WriteLine("⚙️ XStateNet Configuration Editor");
            Console.WriteLine("==================================");
            Console.WriteLine();

            while (true)
            {
                Console.WriteLine("Available commands:");
                Console.WriteLine("  1. list - List all configuration types");
                Console.WriteLine("  2. load <type> - Load configuration");
                Console.WriteLine("  3. edit <type> - Edit configuration");
                Console.WriteLine("  4. validate <type> - Validate configuration");
                Console.WriteLine("  5. template <type> - Create template");
                Console.WriteLine("  6. report - Generate configuration report");
                Console.WriteLine("  7. export <path> - Export all configurations");
                Console.WriteLine("  8. import <path> - Import configurations");
                Console.WriteLine("  9. help - Show this help");
                Console.WriteLine("  10. exit - Exit editor");
                Console.WriteLine();

                Console.Write("config> ");
                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLower();

                try
                {
                    await ExecuteConfigCommand(command, parts.Skip(1).ToArray());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                }

                if (command == "exit")
                    break;

                Console.WriteLine();
            }
        }

        // Private methods
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"🔄 Configuration file changed: {e.Name}");

            // Try to reload configuration
            var fileName = Path.GetFileNameWithoutExtension(e.Name);
            foreach (var (typeName, _) in _configurations.ToList())
            {
                var type = Type.GetType(typeName);
                if (type?.Name == fileName)
                {
                    try
                    {
                        // Reload configuration
                        var method = typeof(ConfigurationManager).GetMethod(nameof(LoadConfiguration));
                        var genericMethod = method!.MakeGenericMethod(type);
                        var newConfig = genericMethod.Invoke(this, new object[] { e.Name });

                        OnConfigurationChanged(new ConfigurationChangedEventArgs
                        {
                            ConfigurationType = type,
                            Configuration = newConfig!,
                            FileName = e.Name,
                            ChangeType = e.ChangeType
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to reload configuration {e.Name}: {ex.Message}");
                    }
                    break;
                }
            }
        }

        private async Task ExecuteConfigCommand(string command, string[] args)
        {
            switch (command)
            {
                case "list":
                    ShowConfigurationTypes();
                    break;

                case "load":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: load <type>");
                        return;
                    }
                    LoadConfigurationByName(args[0]);
                    break;

                case "validate":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: validate <type>");
                        return;
                    }
                    ValidateConfigurationByName(args[0]);
                    break;

                case "template":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: template <type>");
                        return;
                    }
                    CreateTemplateByName(args[0]);
                    break;

                case "report":
                    ShowConfigurationReport();
                    break;

                case "export":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: export <path>");
                        return;
                    }
                    ExportAllConfigurations(args[0]);
                    break;

                case "import":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: import <path>");
                        return;
                    }
                    ImportConfigurations(args[0]);
                    break;

                case "help":
                    // Help already shown above
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }
        }

        private void ShowConfigurationTypes()
        {
            Console.WriteLine("Registered Configuration Types:");
            Console.WriteLine("------------------------------");

            foreach (var (type, schema) in _schemas)
            {
                var isLoaded = _configurations.ContainsKey(type.FullName!);
                var status = isLoaded ? "✅ Loaded" : "⚪ Not loaded";
                Console.WriteLine($"{status} {type.Name}");
                Console.WriteLine($"   Properties: {schema.Properties.Count}");
                if (!string.IsNullOrEmpty(schema.Description))
                {
                    Console.WriteLine($"   Description: {schema.Description}");
                }
                Console.WriteLine();
            }
        }

        private void LoadConfigurationByName(string typeName)
        {
            var type = FindTypeByName(typeName);
            if (type == null)
            {
                Console.WriteLine($"Configuration type '{typeName}' not found.");
                return;
            }

            var method = typeof(ConfigurationManager).GetMethod(nameof(LoadConfiguration));
            var genericMethod = method!.MakeGenericMethod(type);
            var config = genericMethod.Invoke(this, new object[] { null! });

            Console.WriteLine($"✅ Loaded configuration for {type.Name}");
        }

        private void ValidateConfigurationByName(string typeName)
        {
            var type = FindTypeByName(typeName);
            if (type == null)
            {
                Console.WriteLine($"Configuration type '{typeName}' not found.");
                return;
            }

            if (!_configurations.TryGetValue(type.FullName!, out var config))
            {
                Console.WriteLine($"Configuration for '{typeName}' is not loaded. Loading...");
                LoadConfigurationByName(typeName);
                config = _configurations[type.FullName!];
            }

            var method = typeof(ConfigurationManager).GetMethod(nameof(ValidateConfiguration));
            var genericMethod = method!.MakeGenericMethod(type);
            var result = (ValidationResult)genericMethod.Invoke(this, new object[] { config })!;

            if (result.IsValid)
            {
                Console.WriteLine($"✅ Configuration for {type.Name} is valid");
            }
            else
            {
                Console.WriteLine($"❌ Configuration for {type.Name} has validation errors:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"   - {error}");
                }
            }
        }

        private void CreateTemplateByName(string typeName)
        {
            var type = FindTypeByName(typeName);
            if (type == null)
            {
                Console.WriteLine($"Configuration type '{typeName}' not found.");
                return;
            }

            var method = typeof(ConfigurationManager).GetMethod(nameof(CreateTemplate));
            var genericMethod = method!.MakeGenericMethod(type);
            genericMethod.Invoke(this, new object[] { null!, true });
        }

        private void ShowConfigurationReport()
        {
            var report = GenerateReport();

            Console.WriteLine("Configuration Report:");
            Console.WriteLine("====================");
            Console.WriteLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Directory: {report.ConfigurationDirectory}");
            Console.WriteLine($"Registered Types: {report.RegisteredTypes}");
            Console.WriteLine($"Loaded Configurations: {report.LoadedConfigurations}");
            Console.WriteLine();

            foreach (var detail in report.ConfigurationDetails)
            {
                var status = detail.IsLoaded ? "✅" : "⚪";
                var validation = detail.ValidationStatus.IsValid ? "✅" : "❌";

                Console.WriteLine($"{status} {detail.TypeName}");
                Console.WriteLine($"   File: {detail.FileName} (exists: {detail.FileExists})");
                Console.WriteLine($"   Loaded: {detail.IsLoaded}");
                Console.WriteLine($"   Valid: {validation}");

                if (!detail.ValidationStatus.IsValid && detail.ValidationStatus.Errors.Any())
                {
                    Console.WriteLine($"   Errors: {string.Join(", ", detail.ValidationStatus.Errors)}");
                }

                if (detail.LastModified.HasValue)
                {
                    Console.WriteLine($"   Modified: {detail.LastModified:yyyy-MM-dd HH:mm:ss}");
                }

                Console.WriteLine();
            }
        }

        private Type? FindTypeByName(string typeName)
        {
            return _schemas.Keys.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                t.FullName!.EndsWith($".{typeName}", StringComparison.OrdinalIgnoreCase));
        }

        private ConfigurationSchema GenerateSchema<T>() where T : class
        {
            var type = typeof(T);
            var schema = new ConfigurationSchema
            {
                TypeName = type.Name,
                FullTypeName = type.FullName!,
                Description = GetTypeDescription<T>(),
                Properties = new Dictionary<string, PropertySchema>()
            };

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite)
                {
                    var propSchema = new PropertySchema
                    {
                        Name = property.Name,
                        Type = property.PropertyType.Name,
                        IsRequired = property.GetCustomAttribute<RequiredAttribute>() != null,
                        Description = GetPropertyDescription(property),
                        DefaultValue = GetDefaultValue(property)
                    };

                    // Add validation attributes
                    var attributes = property.GetCustomAttributes<ValidationAttribute>();
                    propSchema.ValidationRules = attributes.Select(attr => new ValidationRule
                    {
                        Type = attr.GetType().Name,
                        ErrorMessage = attr.ErrorMessage,
                        Parameters = ExtractValidationParameters(attr)
                    }).ToList();

                    schema.Properties[property.Name] = propSchema;
                }
            }

            return schema;
        }

        private string GetTypeDescription<T>()
        {
            var type = typeof(T);
            var attr = type.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            return attr?.Description ?? $"Configuration for {type.Name}";
        }

        private string GetPropertyDescription(PropertyInfo property)
        {
            var attr = property.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            return attr?.Description ?? "";
        }

        private object? GetDefaultValue(PropertyInfo property)
        {
            var defaultAttr = property.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>();
            return defaultAttr?.Value;
        }

        private Dictionary<string, object> ExtractValidationParameters(ValidationAttribute attribute)
        {
            var parameters = new Dictionary<string, object>();

            switch (attribute)
            {
                case RangeAttribute range:
                    parameters["Minimum"] = range.Minimum;
                    parameters["Maximum"] = range.Maximum;
                    break;
                case StringLengthAttribute stringLength:
                    parameters["MaximumLength"] = stringLength.MaximumLength;
                    parameters["MinimumLength"] = stringLength.MinimumLength;
                    break;
                case RegularExpressionAttribute regex:
                    parameters["Pattern"] = regex.Pattern;
                    break;
            }

            return parameters;
        }

        private ConfigurationDocumentation GenerateDocumentation<T>() where T : new()
        {
            var type = typeof(T);
            var doc = new ConfigurationDocumentation
            {
                TypeName = type.Name,
                Description = GetTypeDescription<T>(),
                Properties = new List<PropertyDocumentation>(),
                Examples = new List<ConfigurationExample>()
            };

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite)
                {
                    var propDoc = new PropertyDocumentation
                    {
                        Name = property.Name,
                        Type = property.PropertyType.Name,
                        Description = GetPropertyDescription(property),
                        IsRequired = property.GetCustomAttribute<RequiredAttribute>() != null,
                        DefaultValue = GetDefaultValue(property),
                        ValidationRules = property.GetCustomAttributes<ValidationAttribute>()
                            .Select(attr => attr.ErrorMessage ?? "Validation rule")
                            .ToList()
                    };

                    doc.Properties.Add(propDoc);
                }
            }

            // Add basic example
            doc.Examples.Add(new ConfigurationExample
            {
                Name = "Default Configuration",
                Description = "Basic configuration with default values",
                Configuration = new T()
            });

            return doc;
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() },
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        private void OnConfigurationChanged(ConfigurationChangedEventArgs e)
        {
            ConfigurationChanged?.Invoke(this, e);
        }

        private void OnValidationFailed(ConfigurationValidationEventArgs e)
        {
            ValidationFailed?.Invoke(this, e);
        }
    }

    // Configuration models and schemas
    public class ConfigurationSchema
    {
        public string TypeName { get; set; } = "";
        public string FullTypeName { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, PropertySchema> Properties { get; set; } = new();
    }

    public class PropertySchema
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsRequired { get; set; }
        public string Description { get; set; } = "";
        public object? DefaultValue { get; set; }
        public List<ValidationRule> ValidationRules { get; set; } = new();
    }

    public class ValidationRule
    {
        public string Type { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class ConfigurationTemplate
    {
        public string ConfigurationType { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public ConfigurationSchema Schema { get; set; } = new();
        public object DefaultConfiguration { get; set; } = new();
        public ConfigurationDocumentation? Documentation { get; set; }
    }

    public class ConfigurationDocumentation
    {
        public string TypeName { get; set; } = "";
        public string Description { get; set; } = "";
        public List<PropertyDocumentation> Properties { get; set; } = new();
        public List<ConfigurationExample> Examples { get; set; } = new();
    }

    public class PropertyDocumentation
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsRequired { get; set; }
        public object? DefaultValue { get; set; }
        public List<string> ValidationRules { get; set; } = new();
    }

    public class ConfigurationExample
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public object Configuration { get; set; } = new();
    }

    public class ConfigurationExport
    {
        public DateTime ExportDate { get; set; }
        public Dictionary<string, object> Configurations { get; set; } = new();
    }

    public class ConfigurationReport
    {
        public DateTime GeneratedAt { get; set; }
        public string ConfigurationDirectory { get; set; } = "";
        public int RegisteredTypes { get; set; }
        public int LoadedConfigurations { get; set; }
        public List<ConfigurationDetail> ConfigurationDetails { get; set; } = new();
    }

    public class ConfigurationDetail
    {
        public string TypeName { get; set; } = "";
        public string FullTypeName { get; set; } = "";
        public string FileName { get; set; } = "";
        public bool FileExists { get; set; }
        public bool IsLoaded { get; set; }
        public ConfigurationSchema Schema { get; set; } = new();
        public ValidationResult ValidationStatus { get; set; } = new();
        public DateTime? LastModified { get; set; }
        public long FileSize { get; set; }
    }

    // Event args
    public class ConfigurationChangedEventArgs : EventArgs
    {
        public Type ConfigurationType { get; set; } = typeof(object);
        public object Configuration { get; set; } = new();
        public string FileName { get; set; } = "";
        public WatcherChangeTypes ChangeType { get; set; }
    }

    public class ConfigurationValidationEventArgs : EventArgs
    {
        public Type ConfigurationType { get; set; } = typeof(object);
        public ValidationResult ValidationResult { get; set; } = new();
    }

    // Validation interfaces and implementations
    public interface IConfigurationValidator
    {
        List<string> Validate(object configuration, ConfigurationSchema schema);
    }

    public class RangeValidator : IConfigurationValidator
    {
        public List<string> Validate(object configuration, ConfigurationSchema schema)
        {
            var errors = new List<string>();

            foreach (var (propName, propSchema) in schema.Properties)
            {
                var rangeRule = propSchema.ValidationRules.FirstOrDefault(r => r.Type == "RangeAttribute");
                if (rangeRule != null)
                {
                    var property = configuration.GetType().GetProperty(propName);
                    if (property != null)
                    {
                        var value = property.GetValue(configuration);
                        if (value is IComparable comparable)
                        {
                            var min = (IComparable)rangeRule.Parameters["Minimum"];
                            var max = (IComparable)rangeRule.Parameters["Maximum"];

                            if (comparable.CompareTo(min) < 0 || comparable.CompareTo(max) > 0)
                            {
                                errors.Add($"{propName}: Value must be between {min} and {max}");
                            }
                        }
                    }
                }
            }

            return errors;
        }
    }

    public class RequiredValidator : IConfigurationValidator
    {
        public List<string> Validate(object configuration, ConfigurationSchema schema)
        {
            var errors = new List<string>();

            foreach (var (propName, propSchema) in schema.Properties)
            {
                if (propSchema.IsRequired)
                {
                    var property = configuration.GetType().GetProperty(propName);
                    if (property != null)
                    {
                        var value = property.GetValue(configuration);
                        if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
                        {
                            errors.Add($"{propName}: Required property is missing or empty");
                        }
                    }
                }
            }

            return errors;
        }
    }

    public class FormatValidator : IConfigurationValidator
    {
        public List<string> Validate(object configuration, ConfigurationSchema schema)
        {
            var errors = new List<string>();

            // Add format validation logic here
            // For example, validate email addresses, URLs, etc.

            return errors;
        }
    }
}