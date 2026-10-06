package com.knit.calculator.report

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Rect
import androidx.core.content.ContextCompat
import com.knit.calculator.R
import java.io.File

/**
 * Логотип фабрики для PDF: файл из папки «Логотип» Google Диска (кэш files/logo.png),
 * а если его нет — встроенный векторный логотип.
 */
object BrandLogo {
    fun file(context: Context) = File(context.filesDir, "logo.png")

    fun save(context: Context, bytes: ByteArray) {
        file(context).writeBytes(bytes)
    }

    fun clear(context: Context) {
        file(context).delete()
    }

    private fun bitmap(context: Context): Bitmap? =
        // Логотип из Диска может быть огромным — в PDF хватает 400 px.
        com.knit.calculator.quote.PhotoStore.loadScaled(file(context).takeIf { it.exists() }?.absolutePath, 400)

    /** Рисует логотип высотой [height]; возвращает занятую ширину. */
    fun draw(context: Context, canvas: Canvas, left: Float, top: Float, height: Float): Float {
        val custom = bitmap(context)
        if (custom != null) {
            val width = height * custom.width / custom.height
            canvas.drawBitmap(custom, null, Rect(left.toInt(), top.toInt(), (left + width).toInt(), (top + height).toInt()), null)
            return width
        }
        val drawable = ContextCompat.getDrawable(context, R.drawable.ic_ks_logo) ?: return 0f
        val width = height * drawable.intrinsicWidth / drawable.intrinsicHeight
        drawable.setBounds(left.toInt(), top.toInt(), (left + width).toInt(), (top + height).toInt())
        drawable.draw(canvas)
        return width
    }
}
