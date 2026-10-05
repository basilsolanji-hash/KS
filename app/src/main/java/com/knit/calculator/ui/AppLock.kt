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
import androidx.compose.ui.Alignment
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

/** Экран блокировки: данные фабрики скрыты до входа. */
@Composable
fun LockScreen(onUnlock: () -> Unit) {
    val colors = LocalKnitColors.current
    LaunchedEffect(Unit) { onUnlock() }
    Column(
        Modifier.fillMaxSize().background(colors.background).padding(32.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterVertically),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Image(
            painterResource(R.drawable.ic_ks_logo), null,
            colorFilter = ColorFilter.tint(colors.textPrimary), modifier = Modifier.height(72.dp),
        )
        Text(stringResource(R.string.lock_title), color = colors.textPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
        Text(stringResource(R.string.lock_subtitle), color = colors.textSecondary, fontSize = 15.sp, textAlign = TextAlign.Center)
        ActionButton(R.string.lock_unlock, R.drawable.ic_lock, primary = true, Modifier.fillMaxWidth(), onClick = onUnlock)
    }
}
