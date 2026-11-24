package com.example.myapplication.data.server

import java.net.Inet4Address
import java.net.NetworkInterface
import java.util.Collections

object NetworkUtils {
    private fun isPrivateIpv4(ip: String): Boolean {
        val parts = ip.split('.')
        if (parts.size != 4) return false
        val b0 = parts[0].toIntOrNull() ?: return false
        val b1 = parts[1].toIntOrNull() ?: return false
        return when {
            b0 == 10 -> true
            b0 == 172 && b1 in 16..31 -> true
            b0 == 192 && b1 == 168 -> true
            else -> false
        }
    }

    /**
     * 端末のローカルIPアドレス（Wi-Fiまたはテザリング・ホットスポット）を取得
     * 192.168.x.x や 10.x.x.x, 172.16-31.x.x などのIPv4アドレスを優先して返す
     */
    fun getLocalIpAddress(): String? {
        try {
            val interfaces = Collections.list(NetworkInterface.getNetworkInterfaces())
            val candidateIps = mutableListOf<String>()

            for (networkInterface in interfaces) {
                if (networkInterface.isLoopback || !networkInterface.isUp || networkInterface.isVirtual) continue

                val name = networkInterface.name.lowercase()
                val addresses = Collections.list(networkInterface.inetAddresses)

                for (inetAddress in addresses) {
                    if (!inetAddress.isLoopbackAddress && !inetAddress.isLinkLocalAddress && inetAddress is Inet4Address) {
                        val hostAddress = inetAddress.hostAddress ?: continue
                        if (name.contains("wlan") || name.contains("ap") || name.contains("hotspot") || name.contains("rndis")) {
                            if (inetAddress.isSiteLocalAddress || isPrivateIpv4(hostAddress)) {
                                return hostAddress
                            }
                            candidateIps.add(0, hostAddress)
                            continue
                        }
                        candidateIps.add(hostAddress)
                    }
                }
            }

            return candidateIps.firstOrNull { ip -> isPrivateIpv4(ip) } ?: candidateIps.firstOrNull()
        } catch (e: Exception) {
            e.printStackTrace()
            return null
        }
    }
}
