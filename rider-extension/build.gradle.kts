plugins {
    kotlin("jvm") version "2.1.20"
    id("org.jetbrains.intellij.platform") version "2.16.0"
}

group = providers.gradleProperty("pluginGroup").get()
version = providers.gradleProperty("pluginVersion").get()

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        rider(providers.gradleProperty("riderVersion").get(), useInstaller = false)
        bundledModule("intellij.rider")
        bundledPlugin("org.jetbrains.plugins.terminal")
        instrumentationTools()
    }
}

kotlin {
    jvmToolchain(17)
}

intellijPlatform {
    pluginConfiguration {
        id = "com.yeahbah.efvibe"
        name = providers.gradleProperty("pluginName")
        version = providers.gradleProperty("pluginVersion")
        description = """
            Evaluate EF Core LINQ in JetBrains Rider by launching the efvibe CLI.
            Run selected queries, inspect dbinfo/tables, start a REPL, and review scan JSON.
        """.trimIndent()
        vendor {
            name = "yeahbah"
            url = "https://myefvibe.com"
        }
        ideaVersion {
            sinceBuild = providers.gradleProperty("sinceBuild")
        }
    }
}
