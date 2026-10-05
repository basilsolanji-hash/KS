package com.knit.calculator.label

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.IOException
import java.util.UUID

/** Сопряжённое Bluetooth-устройство (принтер). */
data class PrinterDevice(val name: String, val address: String)

/** Печать на термопринтер по Bluetooth (последовательный порт SPP). */
object BluetoothPrinter {
    private val SPP: UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")

    /** На Android 12+ нужно разрешение «Устройства поблизости». */
    val permission: String? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) Manifest.permission.BLUETOOTH_CONNECT else null

    fun hasPermission(context: Context): Boolean =
        permission == null || context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED

    private fun adapter(context: Context) = (context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter

    fun isEnabled(context: Context): Boolean = adapter(context)?.isEnabled == true

    /** Сопряжённые устройства: принтеры (класс «Изображение») — первыми. */
    @SuppressLint("MissingPermission") // проверяется hasPermission
    fun bonded(context: Context): List<PrinterDevice> {
        if (!hasPermission(context)) return emptyList()
        val devices = adapter(context)?.bondedDevices.orEmpty()
        return devices.sortedByDescending { it.bluetoothClass?.majorDeviceClass == IMAGING }
            .map { PrinterDevice(it.name ?: it.address, it.address) }
    }

    /** Отправляет команды принтеру; ошибка — исключение с понятным текстом. */
    @SuppressLint("MissingPermission") // проверяется hasPermission
    suspend fun send(context: Context, address: String, data: ByteArray) = withContext(Dispatchers.IO) {
        if (!hasPermission(context)) throw IOException("Нет разрешения на Bluetooth")
        val adapter = adapter(context) ?: throw IOException("На телефоне нет Bluetooth")
        if (!adapter.isEnabled) throw IOException("Включите Bluetooth")
        val device: BluetoothDevice = adapter.getRemoteDevice(address)
        runCatching { adapter.cancelDiscovery() }
        var socket: BluetoothSocket? = null
        try {
            val secure = device.createRfcommSocketToServiceRecord(SPP)
            socket = secure
            val connected = try {
                secure.connect()
                secure
            } catch (e: IOException) {
                // Некоторые принтеры принимают только незащищённое соединение.
                runCatching { secure.close() }
                device.createInsecureRfcommSocketToServiceRecord(SPP).also { socket = it; it.connect() }
            }
            val out = connected.outputStream
            var offset = 0
            while (offset < data.size) {
                val n = minOf(CHUNK, data.size - offset)
                out.write(data, offset, n)
                offset += n
            }
            out.flush()
            // Принтер дочитывает буфер — не закрываем соединение сразу.
            delay(800)
        } catch (e: IOException) {
            throw IOException("Принтер не отвечает: включите его и проверьте сопряжение (${e.message})")
        } finally {
            runCatching { socket?.close() }
        }
    }

    private const val CHUNK = 4096
    private const val IMAGING = 0x0600
}
