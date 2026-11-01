plugins {
    id("com.android.application")
    id("com.google.devtools.ksp")
}

android {
    namespace = "com.example.laptopstatus"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.example.laptopstatus"
        minSdk = 24
        targetSdk = 36
        versionCode = 61
        versionName = "1.18.11"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    buildFeatures {
        buildConfig = true
        viewBinding = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlin {
        compilerOptions {
            jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
        }
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.constraintlayout:constraintlayout:2.1.4")
    implementation("androidx.recyclerview:recyclerview:1.3.2")
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")
    implementation("androidx.appfunctions:appfunctions:1.0.0-alpha12")
    ksp("androidx.appfunctions:appfunctions-compiler:1.0.0-alpha12")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
}
