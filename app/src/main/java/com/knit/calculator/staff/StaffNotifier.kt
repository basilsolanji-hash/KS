package com.knit.calculator.staff

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
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/**
 * Уведомления с сервера фабрики (задачи, комментарии, сроки, просрочки) — без Firebase:
 * телефон сам спрашивает сервер раз в 15 минут (WorkManager) и раз в минуту, пока приложение открыто.
 * Нажатие открывает задачу.
 */
object StaffNotifier {
    private const val CHANNEL = "factory_tasks"
    private const val WORK = "factory-notifications"
    const val EXTRA_TASK = "open_task"

    fun schedule(context: Context) {
        val request = PeriodicWorkRequestBuilder<StaffNotifyWorker>(15, TimeUnit.MINUTES)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(context).enqueueUniquePeriodicWork(WORK, ExistingPeriodicWorkPolicy.KEEP, request)
    }

    fun cancel(context: Context) = WorkManager.getInstance(context).cancelUniqueWork(WORK)

    /** Забрать новые уведомления и показать; `openTasks` — число открытых задач (для главного экрана). */
    suspend fun poll(context: Context): Int? {
        val store = ServerStore(context)
        if (!store.connected) return null
        val r = runCatching { ServerClient(store.url, store.token).call("notifications") }.getOrNull() ?: return null
        val list = r.optJSONArray("notifications") ?: JSONArray()
        val shown = JSONArray()
        for (i in 0 until list.length()) {
            val n = list.optJSONObject(i) ?: continue
            if (show(context, n)) shown.put(n.optInt("id"))
        }
        if (shown.length() > 0) runCatching { ServerClient(store.url, store.token).call("notificationsRead", JSONObject().put("ids", shown)) }
        return r.optInt("openTasks")
    }

    private fun show(context: Context, n: JSONObject): Boolean {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) return false
        val nm = context.getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26 && nm.getNotificationChannel(CHANNEL) == null) {
            nm.createNotificationChannel(NotificationChannel(CHANNEL, context.getString(R.string.notify_channel), NotificationManager.IMPORTANCE_HIGH))
        }
        val taskId = n.optString("ref").toIntOrNull()
        val open = Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        taskId?.let { open.putExtra(EXTRA_TASK, it) }
        val pending = PendingIntent.getActivity(context, n.optInt("id"), open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_quote)
            .setContentTitle(n.optString("title"))
            .setContentText(n.optString("body"))
            .setStyle(NotificationCompat.BigTextStyle().bigText(n.optString("body")))
            .setPriority(if (n.optString("kind") in setOf("late", "task")) NotificationCompat.PRIORITY_HIGH else NotificationCompat.PRIORITY_DEFAULT)
            .setAutoCancel(true)
            .setContentIntent(pending)
            .build()
        return runCatching { NotificationManagerCompat.from(context).notify(10_000 + n.optInt("id"), notification); true }.getOrDefault(false)
    }
}

class StaffNotifyWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        StaffNotifier.poll(applicationContext)
        return Result.success()
    }
}
