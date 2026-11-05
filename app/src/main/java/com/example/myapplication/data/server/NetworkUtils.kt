package com.example.myapplication.data.server

import java.net.Inet4Address
import java.net.NetworkInterface
import java.util.Collections

object NetworkUtils {
    /**
     * 端末のローカルIPアドレス（Wi-Fiまたはテザリング・ホットスポット）を取得
     * 192.168.x.x や 10.x.x.x, 172.16-31.x.x などのIPv4アドレスを優先して返す
     */
    fun getLocalIpAddress(): String? {
        try {
            val interfaces = Collections.list(NetworkInterface.getNetworkInterfaces())
            val candidateIps = mutableListOf<String>()

            for (networkInterface in interfaces) {
                // ループバックや停止中のインターフェースは除外
                if (networkInterface.isLoopback || !networkInterface.isUp) continue

                val name = networkInterface.name.lowercase()
                val addresses = Collections.list(networkInterface.inetAddresses)

                for (inetAddress in addresses) {
                    if (!inetAddress.isLoopbackAddress && inetAddress is Inet4Address) {
                        val hostAddress = inetAddress.hostAddress ?: continue
                        // wlan (Wi-Fi) や ap / rndis (テザリング) を優先候補
                        if (name.contains("wlan") || name.contains("ap") || name.contains("hotspot") || name.contains("rndis")) {
                            return hostAddress
                        }
                        candidateIps.add(hostAddress)
                    }
                }
            }

            // Wi-Fi/テザリングの明示的な名前が見つからなければ、プライベートIPを優先して返却
            return candidateIps.firstOrNull { ip ->
                ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172.")
            } ?: candidateIps.firstOrNull()
        } catch (e: Exception) {
            e.printStackTrace()
            return null
        }
    }
}
