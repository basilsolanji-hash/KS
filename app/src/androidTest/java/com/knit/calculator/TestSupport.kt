package com.knit.calculator

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.pdf.PdfRenderer
import android.os.ParcelFileDescriptor
import androidx.compose.ui.test.junit4.ComposeTestRule
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.rules.TestWatcher
import org.junit.runner.Description
import java.io.File

val targetContext: Context get() = InstrumentationRegistry.getInstrumentation().targetContext

/** Сбрасывает данные приложения перед запуском экрана; [theme] — «LIGHT»/«DARK» для скриншотов. */
class ResetAppRule(private val theme: String = "LIGHT") : TestWatcher() {
    override fun starting(description: Description) {
        listOf("calculator_history", "calculator_settings", "yarn_calculator", "quote", "ops", "labels", "work", "comms", "warehouse", "staff").forEach {
            targetContext.getSharedPreferences(it, Context.MODE_PRIVATE).edit().clear().commit()
        }
        // Скриншоты тестов: запрет снимков экрана выключен.
        targetContext.getSharedPreferences("quote", Context.MODE_PRIVATE).edit().putBoolean("secure_screen", false).commit()
        targetContext.getSharedPreferences("calculator_settings", Context.MODE_PRIVATE).edit().putString("theme", theme).commit()
        File(targetContext.filesDir, "moysklad.json").delete()
    }
}

/** Скриншоты для проверки интерфейса: files/screenshots приложения (забираются через `adb exec-out run-as`). */
object Shots {
    private val dir: File get() = File(targetContext.filesDir, "screenshots").apply { mkdirs() }

    /** Снимок экрана после того, как Compose дорисовал кадр и завершил анимации. */
    fun take(name: String, compose: ComposeTestRule) {
        compose.waitForIdle()
        compose.mainClock.advanceTimeBy(1_500)
        compose.waitForIdle()
        InstrumentationRegistry.getInstrumentation().waitForIdleSync()
        val bitmap = InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot() ?: return
        save(bitmap, name)
    }

    fun save(bitmap: Bitmap, name: String) {
        File(dir, "$name.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
    }

    /** Рендерит страницы PDF в PNG; возвращает число страниц. */
    fun renderPdf(file: File, name: String): Int {
        ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { fd ->
            PdfRenderer(fd).use { renderer ->
                for (i in 0 until renderer.pageCount) {
                    renderer.openPage(i).use { page ->
                        val bitmap = Bitmap.createBitmap(page.width * 2, page.height * 2, Bitmap.Config.ARGB_8888)
                        bitmap.eraseColor(Color.WHITE)
                        page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                        save(bitmap, "${name}_p${i + 1}")
                    }
                }
                return renderer.pageCount
            }
        }
    }
}

/** Раздел из меню ☰ главного экрана. */
fun androidx.compose.ui.test.junit4.ComposeTestRule.openMenu(item: String) {
    onNode(androidx.compose.ui.test.hasContentDescription("Меню")).performClick()
    waitForIdle()
    onNode(androidx.compose.ui.test.hasText(item)).performScrollTo().performClick()
    waitForIdle()
}
