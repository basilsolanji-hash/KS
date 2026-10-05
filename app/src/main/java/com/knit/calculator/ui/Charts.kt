package com.knit.calculator.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.ui.theme.LocalKnitColors
import java.math.BigDecimal

/** Цвета серий: проверены на различимость (в т. ч. при дальтонизме) для светлой и тёмной темы. */
object ChartColors {
    fun inflow(dark: Boolean) = if (dark) Color(0xFF20A99E) else Color(0xFF0E9488)
    fun outflow(dark: Boolean) = if (dark) Color(0xFF8A78EA) else Color(0xFF6E5BD8)
}

/** Месяц «2026-10» → «окт». */
fun shortMonth(key: String): String {
    val names = listOf("янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек")
    return key.substringAfter('-').toIntOrNull()?.let { names.getOrNull(it - 1) } ?: key
}

/** Компактная сумма для подписи: 1,2 млн / 350 тыс. */
fun compactRub(v: BigDecimal): String {
    val d = v.toDouble()
    return when {
        d >= 1_000_000 -> String.format("%.1f млн", d / 1_000_000).replace('.', ',')
        d >= 1_000 -> String.format("%.0f тыс", d / 1_000)
        else -> String.format("%.0f", d)
    }
}

/**
 * Столбцы по месяцам: одна или две серии (рядом), общий ноль, необязательная линия плана.
 * Нажатие на месяц показывает точные суммы над графиком.
 */
@Composable
fun MonthBars(
    title: String,
    months: List<String>,
    series: List<Pair<String, List<BigDecimal>>>,
    colors: List<Color>,
    plan: BigDecimal? = null,
    planLabel: String = "",
    modifier: Modifier = Modifier,
) {
    val theme = LocalKnitColors.current
    var selected by remember(months) { mutableStateOf(months.lastIndex) }
    val max = (series.flatMap { it.second } + listOfNotNull(plan)).maxOrNull()?.takeIf { it.signum() > 0 } ?: BigDecimal.ONE
    Surface(shape = RoundedCornerShape(20.dp), color = theme.panel, modifier = modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(title, color = theme.textSecondary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            // Выбранный месяц: точные суммы (подсказка вместо подписи на каждом столбце).
            val i = selected.coerceIn(0, months.lastIndex.coerceAtLeast(0))
            if (months.isNotEmpty()) {
                Text(
                    shortMonth(months[i]) + ": " + series.joinToString(" · ") { (name, v) -> name + " " + QuoteCalculator.formatMoney(v[i]) + " ₽" },
                    color = theme.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold,
                )
            }
            if (series.size > 1 || plan != null) {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    if (series.size > 1) series.forEachIndexed { s, (name, _) -> Legend(colors[s], name) }
                    if (plan != null) Text("— — $planLabel ${compactRub(plan)}", color = theme.textSecondary, fontSize = 12.sp)
                }
            }
            val grid = theme.textSecondary.copy(alpha = 0.25f)
            val planColor = theme.textSecondary
            Canvas(
                Modifier.fillMaxWidth().height(130.dp)
                    .semantics { contentDescription = title }
                    .pointerInput(months) {
                        detectTapGestures { p ->
                            if (months.isNotEmpty()) selected = (p.x / (size.width.toFloat() / months.size)).toInt().coerceIn(0, months.lastIndex)
                        }
                    },
            ) {
                if (months.isEmpty()) return@Canvas
                val slot = size.width / months.size
                val chartH = size.height
                drawLine(grid, Offset(0f, chartH), Offset(size.width, chartH), strokeWidth = 1.dp.toPx())
                val gap = 2.dp.toPx()
                val barW = ((slot * 0.6f) - gap * (series.size - 1)) / series.size
                months.indices.forEach { m ->
                    if (m == selected) drawRect(grid.copy(alpha = 0.12f), Offset(m * slot, 0f), Size(slot, chartH))
                    series.forEachIndexed { s, (_, values) ->
                        val h = (values[m].toFloat() / max.toFloat()) * (chartH - 4.dp.toPx())
                        val x = m * slot + slot * 0.2f + s * (barW + gap)
                        roundedTopBar(colors[s], x, chartH, barW, h)
                    }
                }
                if (plan != null) {
                    val y = chartH - (plan.toFloat() / max.toFloat()) * (chartH - 4.dp.toPx())
                    drawLine(planColor, Offset(0f, y), Offset(size.width, y), strokeWidth = 2.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(10f, 8f)))
                }
            }
            Row(Modifier.fillMaxWidth()) {
                months.forEachIndexed { m, k ->
                    Text(
                        shortMonth(k), color = if (m == selected) theme.textPrimary else theme.textSecondary, fontSize = 12.sp,
                        fontWeight = if (m == selected) FontWeight.Bold else FontWeight.Normal,
                        modifier = Modifier.weight(1f), textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                    )
                }
            }
        }
    }
}

/** Столбец со скруглённым верхом (4dp), основание — на нуле. */
private fun DrawScope.roundedTopBar(color: Color, x: Float, base: Float, w: Float, h: Float) {
    if (h <= 0f) return
    val r = minOf(4.dp.toPx(), w / 2, h)
    drawRoundRect(color, Offset(x, base - h), Size(w, h), CornerRadius(r, r))
    if (h > r) drawRect(color, Offset(x, base - r), Size(w, r)) // низ — прямой
}

@Composable
private fun Legend(color: Color, label: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(10.dp).padding(0.dp)) {
            Canvas(Modifier.size(10.dp)) { drawRoundRect(color, cornerRadius = CornerRadius(2.dp.toPx())) }
        }
        Text(" $label", color = LocalKnitColors.current.textSecondary, fontSize = 12.sp)
    }
}
