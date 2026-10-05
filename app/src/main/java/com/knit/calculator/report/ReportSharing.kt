package com.knit.calculator.report

import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.print.PrintManager
import android.widget.Toast
import androidx.core.content.FileProvider
import com.knit.calculator.R
import java.io.File
import java.io.FileOutputStream
import java.io.IOException

/** Отправка, печать и сохранение PDF через стандартные средства Android (без доступа к сети). */
object ReportSharing {
    private fun uriFor(context: Context, file: File): Uri =
        FileProvider.getUriForFile(context, "${context.packageName}.reports", file)

    /** Системное меню «Поделиться»: мессенджеры, «Файлы», Google Диск и т. д. */
    fun share(context: Context, file: File, subject: String, text: String) {
        val uri = uriFor(context, file)
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = "application/pdf"
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, subject)
            putExtra(Intent.EXTRA_TEXT, text)
            clipData = ClipData.newRawUri(file.name, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        start(context, Intent.createChooser(intent, context.getString(R.string.yarn_share_title)))
    }

    /**
     * PDF сразу в мессенджер ([packages] — варианты приложения, например Telegram и Telegram X).
     * Приложения нет — обычное меню «Поделиться». `false` — мессенджер не найден.
     */
    fun toApp(context: Context, file: File, subject: String, text: String, packages: List<String>): Boolean {
        val uri = uriFor(context, file)
        val base = Intent(Intent.ACTION_SEND).apply {
            type = "application/pdf"
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, subject)
            putExtra(Intent.EXTRA_TEXT, text)
            clipData = ClipData.newRawUri(file.name, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        for (pkg in packages) {
            try {
                context.startActivity(Intent(base).setPackage(pkg))
                return true
            } catch (e: ActivityNotFoundException) {
                // следующий вариант
            }
        }
        start(context, Intent.createChooser(base, context.getString(R.string.yarn_share_title)))
        return false
    }

    /** Письмо с PDF во вложении; адресатов можно изменить в почтовом приложении. */
    fun email(context: Context, file: File, subject: String, text: String, recipients: Array<String>) {
        val uri = uriFor(context, file)
        val intent = Intent(Intent.ACTION_SEND).apply {
            type = "application/pdf"
            putExtra(Intent.EXTRA_EMAIL, recipients)
            putExtra(Intent.EXTRA_SUBJECT, subject)
            putExtra(Intent.EXTRA_TEXT, text)
            putExtra(Intent.EXTRA_STREAM, uri)
            clipData = ClipData.newRawUri(file.name, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            // Показываем только почтовые приложения.
            selector = Intent(Intent.ACTION_SENDTO, Uri.parse("mailto:"))
        }
        try {
            context.startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            // Нет почтового клиента — предлагаем любое приложение.
            intent.selector = null
            start(context, Intent.createChooser(intent, context.getString(R.string.yarn_email)))
        }
    }

    /** Печать; в системном диалоге можно выбрать «Сохранить как PDF». */
    fun print(context: Context, file: File, jobName: String) {
        val printManager = context.getSystemService(Context.PRINT_SERVICE) as? PrintManager ?: return
        printManager.print(jobName, FilePrintAdapter(file), PrintAttributes.Builder().setMediaSize(PrintAttributes.MediaSize.ISO_A4).build())
    }

    private fun start(context: Context, intent: Intent) {
        try {
            context.startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            Toast.makeText(context, R.string.yarn_no_app, Toast.LENGTH_LONG).show()
        }
    }

    private class FilePrintAdapter(private val file: File) : PrintDocumentAdapter() {
        override fun onLayout(
            oldAttributes: PrintAttributes?,
            newAttributes: PrintAttributes,
            cancellationSignal: CancellationSignal,
            callback: LayoutResultCallback,
            extras: Bundle?,
        ) {
            if (cancellationSignal.isCanceled) {
                callback.onLayoutCancelled()
                return
            }
            val info = PrintDocumentInfo.Builder(file.name)
                .setContentType(PrintDocumentInfo.CONTENT_TYPE_DOCUMENT)
                .build()
            callback.onLayoutFinished(info, oldAttributes != newAttributes)
        }

        override fun onWrite(
            pages: Array<out PageRange>,
            destination: ParcelFileDescriptor,
            cancellationSignal: CancellationSignal,
            callback: WriteResultCallback,
        ) {
            try {
                file.inputStream().use { input ->
                    FileOutputStream(destination.fileDescriptor).use { output -> input.copyTo(output) }
                }
                callback.onWriteFinished(arrayOf(PageRange.ALL_PAGES))
            } catch (e: IOException) {
                callback.onWriteFailed(e.message)
            }
        }
    }
}
