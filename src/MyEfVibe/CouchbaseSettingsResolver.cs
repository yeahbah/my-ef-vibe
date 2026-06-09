using System.Text.Json;

namespace MyEfVibe;

internal static class CouchbaseSettingsResolver
{
    internal static bool TryResolve(string startupProjectPath, out CouchbaseSettings settings)
    {
        settings = new CouchbaseSettings();

        if (string.IsNullOrWhiteSpace(startupProjectPath))
        {
            return false;
        }

        CouchbaseSettings? resolved = null;

        foreach (var settingsPath in EnumerateStartupSettingsPaths(startupProjectPath))
        {
            if (TryReadFromFile(settingsPath, out var fromFile))
            {
                resolved = Merge(resolved, fromFile);
            }
        }

        if (TryReadFromUserSecrets(startupProjectPath, out var fromSecrets))
        {
            resolved = Merge(resolved, fromSecrets);
        }

        if (resolved is null || !resolved.IsComplete)
        {
            return false;
        }

        settings = resolved;

        return true;
    }

    private static CouchbaseSettings Merge(CouchbaseSettings? current, CouchbaseSettings incoming)
    {
        if (current is null)
        {
            return incoming;
        }

        return new CouchbaseSettings
        {
            ConnectionString = Prefer(incoming.ConnectionString, current.ConnectionString),
            Username = Prefer(incoming.Username, current.Username),
            Password = Prefer(incoming.Password, current.Password),
            BucketName = Prefer(incoming.BucketName, current.BucketName),
            ScopeName = Prefer(incoming.ScopeName, current.ScopeName),
            CollectionName = incoming.CollectionName ?? current.CollectionName
        };
    }

    private static string Prefer(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
    }

    private static bool TryReadFromUserSecrets(string startupProjectPath, out CouchbaseSettings settings)
    {
        settings = new CouchbaseSettings();

        if (!CsprojInspector.TryGetUserSecretsId(startupProjectPath, out var userSecretsId))
        {
            return false;
        }

        var secretsRoot = UserSecretsConnectionResolver.GetUserSecretsRootDirectory();

        if (string.IsNullOrEmpty(secretsRoot))
        {
            return false;
        }

        var secretsPath = Path.Combine(secretsRoot, userSecretsId, "secrets.json");

        if (!File.Exists(secretsPath))
        {
            return false;
        }

        return TryReadFlatSecrets(secretsPath, out settings);
    }

    private static bool TryReadFlatSecrets(string secretsPath, out CouchbaseSettings settings)
    {
        settings = new CouchbaseSettings();

        if (!ConfigurationJson.TryParseFile(secretsPath, out var document) || document is null)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            var merged = new CouchbaseSettings();

            if (TryReadSection(root, CouchbaseSettings.SettingsRootName, out var couchbaseSection))
            {
                merged = Merge(merged, couchbaseSection);
            }

            if (TryReadSection(root, CouchbaseSettings.LegacySettingsRootName, out var legacySection))
            {
                merged = Merge(merged, legacySection);
            }

            merged = Merge(merged, ReadFlatKeys(root, CouchbaseSettings.SettingsRootName));

            if (TryReadSection(root, CouchbaseSettings.LegacySettingsRootName, out _))
            {
                merged = Merge(merged, ReadFlatKeys(root, CouchbaseSettings.LegacySettingsRootName));
            }

            if (!merged.IsComplete && !HasAnyValue(merged))
            {
                return false;
            }

            settings = merged;

            return merged.IsComplete;
        }
    }

    private static CouchbaseSettings ReadFlatKeys(JsonElement root, string prefix)
    {
        return new CouchbaseSettings
        {
            ConnectionString = ReadFlatString(root, $"{prefix}:ConnectionString"),
            Username = ReadFlatString(root, $"{prefix}:Username"),
            Password = ReadFlatString(root, $"{prefix}:Password"),
            BucketName = ReadFlatString(root, $"{prefix}:BucketName"),
            ScopeName = ReadFlatString(root, $"{prefix}:ScopeName"),
            CollectionName = ReadFlatString(root, $"{prefix}:CollectionName")
        };
    }

    private static bool TryReadFromFile(string settingsPath, out CouchbaseSettings settings)
    {
        settings = new CouchbaseSettings();

        if (!ConfigurationJson.TryParseFile(settingsPath, out var document) || document is null)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            var merged = new CouchbaseSettings();

            if (TryReadSection(root, CouchbaseSettings.SettingsRootName, out var couchbaseSection))
            {
                merged = Merge(merged, couchbaseSection);
            }

            if (TryReadSection(root, CouchbaseSettings.LegacySettingsRootName, out var legacySection))
            {
                merged = Merge(merged, legacySection);
            }

            if (!HasAnyValue(merged))
            {
                return false;
            }

            settings = merged;

            return true;
        }
    }

    private static bool TryReadSection(JsonElement root, string sectionName, out CouchbaseSettings settings)
    {
        settings = new CouchbaseSettings();

        if (!root.TryGetProperty(sectionName, out var section)
            || section.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        settings = new CouchbaseSettings
        {
            ConnectionString = ReadProperty(section, "ConnectionString"),
            Username = ReadProperty(section, "Username"),
            Password = ReadProperty(section, "Password"),
            BucketName = ReadProperty(section, "BucketName"),
            ScopeName = ReadProperty(section, "ScopeName"),
            CollectionName = ReadNullableProperty(section, "CollectionName")
        };

        return HasAnyValue(settings);
    }

    private static bool HasAnyValue(CouchbaseSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.ConnectionString)
               || !string.IsNullOrWhiteSpace(settings.Username)
               || !string.IsNullOrWhiteSpace(settings.Password)
               || !string.IsNullOrWhiteSpace(settings.BucketName)
               || !string.IsNullOrWhiteSpace(settings.ScopeName)
               || !string.IsNullOrWhiteSpace(settings.CollectionName);
    }

    private static string ReadProperty(JsonElement section, string propertyName)
    {
        return section.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? ReadNullableProperty(JsonElement section, string propertyName)
    {
        return section.TryGetProperty(propertyName, out var value)
            ? value.GetString()
            : null;
    }

    private static string ReadFlatString(JsonElement root, string key)
    {
        return root.TryGetProperty(key, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static IEnumerable<string> EnumerateStartupSettingsPaths(string startupProjectPath)
    {
        var startupDirectory = Path.GetDirectoryName(startupProjectPath)!;

        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var candidate = Path.Combine(startupDirectory, fileName);

            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }
}
