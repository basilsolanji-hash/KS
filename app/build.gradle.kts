plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "com.knit.calculator"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.knit.calculator"
        minSdk = 24
        targetSdk = 35
        versionCode = 5
        versionName = "1.3.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    // Постоянный ключ фабрики: CI кладёт его из секретов репозитория (см. .github/workflows/android.yml).
    // Без него — отладочный ключ, такой APK не обновит версию, подписанную ключом фабрики.
    val ksFile = System.getenv("KS_KEYSTORE_FILE")?.let(::file)?.takeIf { it.exists() }
    val ksPassword = System.getenv("KS_KEYSTORE_PASSWORD")
    if (ksFile != null && !ksPassword.isNullOrEmpty()) {
        signingConfigs.create("ks") {
            storeFile = ksFile
            storePassword = ksPassword
            keyAlias = "ks"
            keyPassword = ksPassword
        }
    }
    val signing = signingConfigs.findByName("ks") ?: signingConfigs.getByName("debug")

    buildTypes {
        debug {
            signingConfig = signing
        }
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            signingConfig = signing
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }
}

dependencies {
    implementation(project(":core"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.work.runtime)
    debugImplementation(libs.androidx.compose.ui.tooling)

    testImplementation(libs.junit)
    testImplementation(libs.org.json) // настоящая реализация org.json для JVM-тестов

    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
    debugImplementation(libs.androidx.compose.ui.test.manifest)
}
