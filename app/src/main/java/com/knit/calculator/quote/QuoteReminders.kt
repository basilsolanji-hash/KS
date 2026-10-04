package com.knit.calculator.quote

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.work.ExistingWorkPolicy
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import com.knit.calculator.MainActivity
import com.knit.calculator.R
import com.knit.calculator.core.QuoteStatus
import java.util.Calendar
import java.util.concurrent.TimeUnit

/**
 * Напоминания об окончании срока КП: за N дней и в последний день, в 10:00.
 * Не приходят, если статус КП уже не «Отправлено» (согласовано, оплачено, отказ…).
 */
object QuoteReminders {
    private const val CHANNEL = "quote_reminders"

    fun schedule(context: Context, quoteId: String, number: Int, client: String, validUntil: Long, daysBefore: Int) {
        val wm = WorkManager.getInstance(context)
        val lastDay = at10(validUntil, 0)
        listOf("before" to at10(validUntil, daysBefore), "last" to lastDay).forEach { (kind, time) ->
            val name = "quote-$quoteId-$kind"
            val delay = time - System.currentTimeMillis()
            if (delay <= 0 || (kind == "before" && daysBefore <= 0)) {
                wm.cancelUniqueWork(name)
                return@forEach
            }
            val days = if (kind == "last") 0 else daysBefore
            val request = OneTimeWorkRequestBuilder<QuoteReminderWorker>()
                .setInitialDelay(delay, TimeUnit.MILLISECONDS)
                .setInputData(workDataOf("id" to quoteId, "number" to number, "client" to client, "days" to days))
                .build()
            wm.enqueueUniqueWork(name, ExistingWorkPolicy.REPLACE, request)
        }
    }

    fun cancel(context: Context, quoteId: String) {
        val wm = WorkManager.getInstance(context)
        wm.cancelUniqueWork("quote-$quoteId-before")
        wm.cancelUniqueWork("quote-$quoteId-last")
    }

    private fun at10(validUntil: Long, daysBefore: Int): Long = Calendar.getInstance().apply {
        timeInMillis = validUntil
        add(Calendar.DAY_OF_YEAR, -daysBefore)
        set(Calendar.HOUR_OF_DAY, 10)
        set(Calendar.MINUTE, 0)
        set(Calendar.SECOND, 0)
    }.timeInMillis

    fun notify(context: Context, quoteId: String, number: Int, client: String, days: Int) {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) return
        val manager = context.getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL, context.getString(R.string.reminder_channel), NotificationManager.IMPORTANCE_DEFAULT),
            )
        }
        val intent = PendingIntent.getActivity(
            context, quoteId.hashCode(),
            Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val title = context.getString(R.string.reminder_title, number)
        val whom = client.ifBlank { context.getString(R.string.reminder_no_client) }
        val text = when (days) {
            0 -> context.getString(R.string.reminder_today, whom)
            1 -> context.getString(R.string.reminder_tomorrow, whom)
            else -> context.getString(R.string.reminder_days, whom, days)
        }
        val notification = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_quote)
            .setContentTitle(title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(intent)
            .setAutoCancel(true)
            .build()
        NotificationManagerCompat.from(context).notify(quoteId.hashCode(), notification)
    }
}

class QuoteReminderWorker(context: Context, params: WorkerParameters) : Worker(context, params) {
    override fun doWork(): Result {
        val id = inputData.getString("id") ?: return Result.success()
        val archived = QuoteStore(applicationContext).loadArchive().firstOrNull { it.id == id }
        // Клиент уже ответил — напоминание не нужно.
        if (archived != null && archived.status != QuoteStatus.SENT) return Result.success()
        QuoteReminders.notify(
            applicationContext, id,
            inputData.getInt("number", 0),
            inputData.getString("client").orEmpty(),
            inputData.getInt("days", 0),
        )
        return Result.success()
    }
}
