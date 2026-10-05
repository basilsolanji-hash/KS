package com.knit.calculator.ui

import android.content.Context
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricManager.Authenticators.BIOMETRIC_WEAK
import androidx.biometric.BiometricManager.Authenticators.DEVICE_CREDENTIAL
import androidx.biometric.BiometricPrompt
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import com.knit.calculator.R
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.theme.LocalKnitColors

/** Вход по отпечатку, лицу или PIN / графическому ключу телефона. */
object AppLock {
    private const val AUTH = BIOMETRIC_WEAK or DEVICE_CREDENTIAL

    /** На телефоне настроена блокировка экрана (иначе защищать нечем). */
    fun available(context: Context): Boolean =
        BiometricManager.from(context).canAuthenticate(AUTH) == BiometricManager.BIOMETRIC_SUCCESS

    fun prompt(activity: FragmentActivity, onSuccess: () -> Unit) {
        val prompt = BiometricPrompt(
            activity,
            ContextCompat.getMainExecutor(activity),
            object : BiometricPrompt.AuthenticationCallback() {
                override fun onAuthenticationSucceeded(result: BiometricPrompt.AuthenticationResult) = onSuccess()
            },
        )
        val info = BiometricPrompt.PromptInfo.Builder()
            .setTitle(activity.getString(R.string.lock_title))
            .setSubtitle(activity.getString(R.string.lock_subtitle))
            .setAllowedAuthenticators(AUTH)
            .build()
        prompt.authenticate(info)
    }
}

/**
 * Экран блокировки: данные фабрики скрыты до входа.
 * Есть свой PIN — цифровая клавиатура (и отпечаток, если включён); нет — сразу запрос отпечатка / PIN телефона.
 */
@Composable
fun LockScreen(onBiometric: (() -> Unit)?, onUnlocked: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = androidx.compose.ui.platform.LocalContext.current
    val store = androidx.compose.runtime.remember { com.knit.calculator.quote.QuoteStore(context) }
    LaunchedEffect(Unit) { onBiometric?.invoke() }
    Column(
        Modifier.fillMaxSize().background(colors.background).padding(32.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Image(
            painterResource(R.drawable.ic_ks_logo), null,
            colorFilter = ColorFilter.tint(colors.textPrimary), modifier = Modifier.height(64.dp),
        )
        if (!store.pinSet) {
            Text(stringResource(R.string.lock_title), color = colors.textPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
            Text(stringResource(R.string.lock_subtitle), color = colors.textSecondary, fontSize = 15.sp, textAlign = TextAlign.Center)
            ActionButton(R.string.lock_unlock, R.drawable.ic_lock, primary = true, Modifier.fillMaxWidth(), onClick = { onBiometric?.invoke() })
            return@Column
        }
        var error by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf<String?>(null) }
        var now by androidx.compose.runtime.remember { androidx.compose.runtime.mutableLongStateOf(System.currentTimeMillis()) }
        val attempts = androidx.compose.runtime.remember(error, now) { store.pinAttempts }
        val paused = attempts.blocked(now)
        if (paused) {
            LaunchedEffect(attempts.blockedUntil) {
                while (System.currentTimeMillis() < attempts.blockedUntil) {
                    now = System.currentTimeMillis()
                    kotlinx.coroutines.delay(500)
                }
                now = System.currentTimeMillis()
                error = null
            }
        }
        val wrong = stringResource(R.string.pin_wrong)
        PinPad(
            title = stringResource(R.string.pin_enter),
            error = if (paused) stringResource(R.string.pin_paused, ((attempts.blockedUntil - now + 999) / 1000).toInt()) else error,
            enabled = !paused,
            onBiometric = onBiometric,
        ) { pin ->
            if (store.checkPin(pin)) {
                store.pinAttempts = com.knit.calculator.core.PinLock.Attempts()
                onUnlocked()
            } else {
                val next = store.pinAttempts.failed(System.currentTimeMillis())
                store.pinAttempts = next
                now = System.currentTimeMillis()
                error = if (next.blocked(now)) null else wrong.format(next.left())
            }
        }
    }
}

/** Ввод PIN: четыре точки и цифровая клавиатура; по четвёртой цифре — [onComplete]. */
@Composable
fun PinPad(
    title: String,
    error: String?,
    enabled: Boolean = true,
    onBiometric: (() -> Unit)? = null,
    onComplete: (String) -> Unit,
) {
    val colors = LocalKnitColors.current
    var pin by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf("") }
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(14.dp)) {
        Text(title, color = colors.textPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
        androidx.compose.foundation.layout.Row(
            horizontalArrangement = Arrangement.spacedBy(16.dp),
            modifier = Modifier.semantics { contentDescription = "Введено цифр: ${pin.length}" },
        ) {
            repeat(com.knit.calculator.core.PinLock.LENGTH) { k ->
                androidx.compose.foundation.Canvas(Modifier.size(16.dp)) {
                    if (k < pin.length) drawCircle(colors.textPrimary)
                    else drawCircle(colors.textSecondary, style = androidx.compose.ui.graphics.drawscope.Stroke(2.dp.toPx()))
                }
            }
        }
        Text(error ?: " ", color = androidx.compose.ui.graphics.Color(0xFFD9534F), fontSize = 14.sp, textAlign = TextAlign.Center)
        val keys = listOf("1", "2", "3", "4", "5", "6", "7", "8", "9", "bio", "0", "del")
        keys.chunked(3).forEach { row ->
            androidx.compose.foundation.layout.Row(horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                row.forEach { key ->
                    val label = when (key) {
                        "bio" -> if (onBiometric != null) "☝" else ""
                        "del" -> "⌫"
                        else -> key
                    }
                    androidx.compose.material3.Surface(
                        onClick = {
                            when (key) {
                                "bio" -> onBiometric?.invoke()
                                "del" -> pin = pin.dropLast(1)
                                else -> if (pin.length < com.knit.calculator.core.PinLock.LENGTH) {
                                    pin += key
                                    if (pin.length == com.knit.calculator.core.PinLock.LENGTH) {
                                        val full = pin
                                        pin = ""
                                        onComplete(full)
                                    }
                                }
                            }
                        },
                        enabled = enabled && label.isNotEmpty(),
                        shape = androidx.compose.foundation.shape.CircleShape,
                        color = if (label.isEmpty() || key == "bio" || key == "del") androidx.compose.ui.graphics.Color.Transparent else colors.panel,
                        modifier = Modifier.size(72.dp).semantics {
                            contentDescription = when (key) { "bio" -> "Отпечаток"; "del" -> "Стереть"; else -> key }
                        },
                    ) {
                        androidx.compose.foundation.layout.Box(contentAlignment = Alignment.Center) {
                            Text(label, color = if (enabled) colors.textPrimary else colors.textSecondary, fontSize = 26.sp, fontWeight = FontWeight.Medium)
                        }
                    }
                }
            }
        }
    }
}
