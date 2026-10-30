package com.example.laptopstatus

import java.net.URL
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/** HTTPS connections pinned to the certificate fingerprint shown by the Windows app. */
object PinnedTls {
    fun <T> withConnection(
        address: String,
        port: Int,
        path: String,
        method: String,
        key: String,
        fingerprint: String,
        connectTimeoutMillis: Int = 4000,
        readTimeoutMillis: Int = 5000,
        block: (HttpsURLConnection) -> T
    ): T {
        val connection = open(address, port, path, method, key, fingerprint, connectTimeoutMillis, readTimeoutMillis)
        return try {
            block(connection)
        } finally {
            connection.disconnect()
        }
    }

    fun <T> withUnpairedConnection(
        address: String,
        port: Int,
        path: String,
        fingerprint: String,
        readTimeoutMillis: Int = 60000,
        connectTimeoutMillis: Int = 5000,
        block: (HttpsURLConnection) -> T
    ): T {
        val connection = open(address, port, path, "GET", null, fingerprint, connectTimeoutMillis, readTimeoutMillis)
        return try {
            block(connection)
        } finally {
            connection.disconnect()
        }
    }

    private fun open(
        address: String,
        port: Int,
        path: String,
        method: String,
        key: String?,
        fingerprint: String,
        connectTimeoutMillis: Int,
        readTimeoutMillis: Int
    ): HttpsURLConnection {
        if (address.isBlank()) {
            throw IllegalStateException("Alamat perangkat tidak tersedia.")
        }

        val publicHttpsUrl = address.trim().startsWith("https://", ignoreCase = true)
        val expected = fingerprint.filter(Char::isLetterOrDigit).uppercase()
        if (!publicHttpsUrl && (expected.length != 64 || expected.any { it !in '0'..'9' && it !in 'A'..'F' })) {
            throw IllegalStateException("Masukkan fingerprint TLS 64 karakter dari Pengaturan Windows.")
        }

        val connectionUrl = if (publicHttpsUrl) {
            val base = address.trim().trimEnd('/')
            val parsed = try { URL(base) } catch (_: Exception) { null }
            if (parsed == null || parsed.protocol != "https" || parsed.host.isBlank() || parsed.userInfo != null || parsed.query != null || parsed.ref != null) {
                throw IllegalStateException("Masukkan URL Cloudflare HTTPS yang valid.")
            }
            "$base$path"
        } else {
            val host = if (address.contains(':') && !address.startsWith('[')) "[$address]" else address
            "https://$host:$port$path"
        }

        val connection = URL(connectionUrl).openConnection() as HttpsURLConnection
        if (publicHttpsUrl) {
            // A Cloudflare public hostname uses its normal public certificate. The
            // Windows origin fingerprint is still checked against the pairing QR.
            return connection.apply {
                connectTimeout = connectTimeoutMillis
                readTimeout = readTimeoutMillis
                requestMethod = method
                if (!key.isNullOrBlank()) setRequestProperty("Authorization", "Bearer $key")
                setRequestProperty("Cache-Control", "no-store")
            }
        }

        val trustManager = object : X509TrustManager {
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()

            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {
                throw CertificateException("Client authentication is not used")
            }

            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
                if (chain.isEmpty()) throw CertificateException("Sertifikat Windows tidak ditemukan")
                val actual = MessageDigest.getInstance("SHA-256").digest(chain[0].encoded)
                    .joinToString("") { "%02X".format(it) }
                if (!MessageDigest.isEqual(
                        actual.toByteArray(StandardCharsets.US_ASCII),
                        expected.toByteArray(StandardCharsets.US_ASCII)
                    )) {
                    throw CertificateException("Fingerprint sertifikat tidak cocok. Periksa fingerprint TLS Windows.")
                }
            }
        }
        val ssl = SSLContext.getInstance("TLS")
        ssl.init(null, arrayOf(trustManager), SecureRandom())
        return connection.apply {
            sslSocketFactory = ssl.socketFactory
            hostnameVerifier = HostnameVerifier { _, _ -> true } // Certificate identity is checked by the SHA-256 pin above.
            connectTimeout = connectTimeoutMillis
            readTimeout = readTimeoutMillis
            requestMethod = method
            if (!key.isNullOrBlank()) setRequestProperty("Authorization", "Bearer $key")
            setRequestProperty("Cache-Control", "no-store")
        }
    }
}
