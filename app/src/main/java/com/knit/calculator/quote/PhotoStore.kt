package com.knit.calculator.quote

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File

/** Фото образцов: хранятся на этом телефоне (files/photos), уменьшены до 1280 px. */
object PhotoStore {
    private const val MAX_SIDE = 1280

    private fun dir(context: Context) = File(context.filesDir, "photos").apply { mkdirs() }

    /** Временный файл для камеры и его content:// адрес. */
    fun cameraTarget(context: Context): Pair<File, Uri> {
        val file = File(File(context.cacheDir, "camera").apply { mkdirs() }, "capture.jpg")
        return file to FileProvider.getUriForFile(context, "${context.packageName}.reports", file)
    }

    /** Копирует и уменьшает фото; возвращает путь или `null`, если фото не читается. */
    fun import(context: Context, uri: Uri, lineId: Long): String? = try {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        context.contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
        var sample = 1
        while (bounds.outWidth / (sample * 2) >= MAX_SIDE || bounds.outHeight / (sample * 2) >= MAX_SIDE) sample *= 2
        val decoded = context.contentResolver.openInputStream(uri)?.use {
            BitmapFactory.decodeStream(it, null, BitmapFactory.Options().apply { inSampleSize = sample })
        } ?: error("decode")
        val rotation = context.contentResolver.openInputStream(uri)?.use {
            when (ExifInterface(it).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)) {
                ExifInterface.ORIENTATION_ROTATE_90 -> 90f
                ExifInterface.ORIENTATION_ROTATE_180 -> 180f
                ExifInterface.ORIENTATION_ROTATE_270 -> 270f
                else -> 0f
            }
        } ?: 0f
        val bitmap = scale(rotate(decoded, rotation))
        val file = File(dir(context), "$lineId-${System.currentTimeMillis()}.jpg")
        file.outputStream().use { bitmap.compress(Bitmap.CompressFormat.JPEG, 85, it) }
        file.absolutePath
    } catch (e: Throwable) {
        null
    }

    fun load(path: String?): Bitmap? = loadScaled(path, MAX_SIDE)

    /** Уменьшенная копия для PDF и списков: фото с телефона могут быть 4000 px — целиком не помещаются в память. */
    fun loadScaled(path: String?, maxSide: Int): Bitmap? = try {
        path?.takeIf { File(it).exists() }?.let { p ->
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(p, bounds)
            var sample = 1
            while (bounds.outWidth / (sample * 2) >= maxSide || bounds.outHeight / (sample * 2) >= maxSide) sample *= 2
            BitmapFactory.decodeFile(p, BitmapFactory.Options().apply { inSampleSize = sample })
        }
    } catch (e: Throwable) {
        null
    }

    /** Сохраняет фото из байтов (МойСклад) уменьшенным до 1280 px; `null` — не картинка. */
    fun saveBytes(context: Context, bytes: ByteArray, lineId: Long): String? = try {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        var sample = 1
        while (bounds.outWidth / (sample * 2) >= MAX_SIDE || bounds.outHeight / (sample * 2) >= MAX_SIDE) sample *= 2
        val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample })?.let(::scale)
            ?: error("decode")
        val file = File(dir(context), "$lineId-ms-${System.currentTimeMillis()}.jpg")
        file.outputStream().use { bitmap.compress(Bitmap.CompressFormat.JPEG, 85, it) }
        file.absolutePath
    } catch (e: Throwable) {
        null
    }

    fun delete(path: String?) {
        path?.let { File(it).delete() }
    }

    private fun rotate(b: Bitmap, degrees: Float): Bitmap =
        if (degrees == 0f) b else Bitmap.createBitmap(b, 0, 0, b.width, b.height, Matrix().apply { postRotate(degrees) }, true)

    private fun scale(b: Bitmap): Bitmap {
        val max = maxOf(b.width, b.height)
        if (max <= MAX_SIDE) return b
        val k = MAX_SIDE.toFloat() / max
        return Bitmap.createScaledBitmap(b, (b.width * k).toInt(), (b.height * k).toInt(), true)
    }
}
