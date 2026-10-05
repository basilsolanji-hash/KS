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
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.knit.calculator.MainActivity
import com.knit.calculator.R
import com.knit.calculator.core.Dashboard
import com.knit.calculator.core.Deal
import com.knit.calculator.core.OrderStage
import com.knit.calculator.core.QuoteCalculator
import java.math.BigDecimal
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale
import java.util.concurrent.TimeUnit

/**
 * Ежедневная сводка в 9:00 (если подключена таблица): долги клиентов, отгрузки завтра,
 * просроченные заказы и КП без ответа больше 3 дней. Ничего важного — уведомления нет.
 */
object DailyDigest {
    private const val CHANNEL = "daily_digest"
    private const val WORK = "daily-digest"

    fun schedule(context: Context, enabled: Boolean) {
        val wm = WorkManager.getInstance(context)
        if (!enabled) {
            wm.cancelUniqueWork(WORK)
            return
        }
        val next = Calendar.getInstance().apply {
            set(Calendar.HOUR_OF_DAY, 9)
            set(Calendar.MINUTE, 0)
            set(Calendar.SECOND, 0)
            if (timeInMillis <= System.currentTimeMillis()) add(Calendar.DAY_OF_YEAR, 1)
        }.timeInMillis
        val request = PeriodicWorkRequestBuilder<DailyDigestWorker>(1, TimeUnit.DAYS)
            .setInitialDelay(next - System.currentTimeMillis(), TimeUnit.MILLISECONDS)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        wm.enqueueUniquePeriodicWork(WORK, ExistingPeriodicWorkPolicy.KEEP, request)
    }

    /** Строки сводки; пусто — уведомлять не о чем. */
    fun lines(context: Context, deals: List<Deal>, sentAt: Map<String, Long>, ops: OpsData, now: Long): List<String> {
        val day = Dashboard.summary(deals, sentAt, ops.paymentsForDebts, ops.orders, now)
        val tomorrowEnd = Calendar.getInstance().apply {
            timeInMillis = now
            add(Calendar.DAY_OF_YEAR, 1)
            set(Calendar.HOUR_OF_DAY, 23)
            set(Calendar.MINUTE, 59)
        }.timeInMillis
        val shipSoon = ops.orders.count { it.stage != OrderStage.SHIPPED && it.due in now..tomorrowEnd }
        return buildList {
            if (day.debt.signum() > 0) add(context.getString(R.string.day_debt, QuoteCalculator.formatMoney(day.debt), day.debtors))
            if (shipSoon > 0) add(context.getString(R.string.digest_ship, shipSoon))
            if (day.overdueOrders > 0) add(context.getString(R.string.day_overdue, day.overdueOrders))
            if (day.waitingQuotes > 0) add(context.getString(R.string.day_waiting, day.waitingQuotes))
        }
    }

    fun notify(context: Context, lines: List<String>) {
        if (lines.isEmpty()) return
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) return
        if (Build.VERSION.SDK_INT >= 26) {
            context.getSystemService(NotificationManager::class.java).createNotificationChannel(
                NotificationChannel(CHANNEL, context.getString(R.string.digest_channel), NotificationManager.IMPORTANCE_DEFAULT),
            )
        }
        val intent = PendingIntent.getActivity(
            context, WORK.hashCode(),
            Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val text = lines.joinToString("\n")
        val notification = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_quote)
            .setContentTitle(context.getString(R.string.digest_title))
            .setContentText(lines.first())
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(intent)
            .setAutoCancel(true)
            .build()
        NotificationManagerCompat.from(context).notify(WORK.hashCode(), notification)
    }
}

class DailyDigestWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        val store = QuoteStore(applicationContext)
        val config = store.loadSyncConfig()
        if (!config.enabled || !store.digestEnabled) return Result.success()
        return try {
            val client = SheetClient(config)
            val quotes = client.quotes(light = true)
            val ops = OpsJson.parse(client.ops())
            val parse = SimpleDateFormat("dd.MM.yyyy", Locale.US)
            val deals = quotes.map { Deal(it.id, it.number, it.client, BigDecimal.valueOf(it.total), it.status) }
            val sentAt = quotes.associate { q -> q.id to (runCatching { parse.parse(q.date.substringBefore(' '))?.time }.getOrNull() ?: System.currentTimeMillis()) }
            DailyDigest.notify(applicationContext, DailyDigest.lines(applicationContext, deals, sentAt, ops, System.currentTimeMillis()))
            Result.success()
        } catch (e: Exception) {
            Result.retry()
        }
    }
}
