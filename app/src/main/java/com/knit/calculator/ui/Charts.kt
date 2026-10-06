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
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.graphics.toArgb
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

/** Целое число с разделением разрядов: 1 234 567. */
fun wholeNumber(v: BigDecimal): String =
    java.text.NumberFormat.getIntegerInstance(java.util.Locale("ru")).format(v.setScale(0, java.math.RoundingMode.HALF_UP))

/**
 * Современный график динамики: плавные линии с градиентной заливкой, крупное значение
 * выбранного месяца и изменение к предыдущему (▲/▼), необязательная линия плана
 * и переключатель единиц (₽ / шт). Нажатие на месяц выбирает его.
 */
@Composable
fun TrendChart(
    title: String,
    labels: List<String>,
    series: List<Pair<String, List<BigDecimal>>>,
    colors: List<Color>,
    /** Прошлый период по каждой серии (рисуется пунктиром на фоне); `null` — не показывать. */
    previous: List<List<BigDecimal>?> = emptyList(),
    plan: BigDecimal? = null,
    planLabel: String = "",
    unit: String = "₽",
    toggle: Pair<List<String>, Int>? = null,
    onToggle: (Int) -> Unit = {},
    modifier: Modifier = Modifier,
) {
    val theme = LocalKnitColors.current
    var selected by remember(labels) { mutableStateOf(labels.lastIndex) }
    val reveal = remember { androidx.compose.animation.core.Animatable(0f) }
    androidx.compose.runtime.LaunchedEffect(series) {
        reveal.snapTo(0f)
        reveal.animateTo(1f, androidx.compose.animation.core.tween(500))
    }
    val max = (series.flatMap { it.second } + previous.filterNotNull().flatten() + listOfNotNull(plan))
        .maxOrNull()?.takeIf { it.signum() > 0 } ?: BigDecimal.ONE
    val fmt: (BigDecimal) -> String = { wholeNumber(it) + " " + unit }
    fun pct(now: BigDecimal, before: BigDecimal): Int? =
        if (before.signum() <= 0) null else now.subtract(before).multiply(BigDecimal(100)).divide(before, 0, java.math.RoundingMode.HALF_UP).toInt()
    Surface(shape = RoundedCornerShape(20.dp), color = theme.panel, modifier = modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(title, color = theme.textSecondary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                if (toggle != null) Segmented(toggle.first, toggle.second, onToggle)
            }
            if (labels.isEmpty()) return@Column
            val i = selected.coerceIn(0, labels.lastIndex)
            // Крупно — итог периода и изменение к прошлому периоду; ниже — выбранный столбец.
            series.forEachIndexed { s, (name, v) ->
                val total = v.fold(BigDecimal.ZERO, BigDecimal::add)
                val before = previous.getOrNull(s)?.fold(BigDecimal.ZERO, BigDecimal::add)
                Row(verticalAlignment = Alignment.Bottom) {
                    if (series.size > 1) Canvas(Modifier.padding(bottom = 7.dp, end = 6.dp).size(8.dp)) { drawCircle(colors[s]) }
                    Text(
                        fmt(total), color = theme.textPrimary,
                        fontSize = if (series.size > 1) 18.sp else 24.sp, fontWeight = FontWeight.Bold,
                        modifier = Modifier.semantics { contentDescription = "$name: ${fmt(total)}" },
                    )
                    if (series.size > 1) Text("  $name", color = theme.textSecondary, fontSize = 13.sp, modifier = Modifier.padding(bottom = 2.dp))
                    pct(total, before ?: BigDecimal.ZERO)?.let { p ->
                        Text(
                            (if (p >= 0) "  ▲ " else "  ▼ ") + kotlin.math.abs(p) + "% к прошлому",
                            color = if (p >= 0) Color(0xFF2E9E5B) else Color(0xFFD9534F),
                            fontSize = 12.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(bottom = 3.dp),
                        )
                    }
                }
            }
            Text(
                labels[i] + ": " + series.joinToString(" · ") { (_, v) -> fmt(v.getOrNull(i) ?: BigDecimal.ZERO) } +
                    (previous.firstOrNull()?.getOrNull(i)?.let { " (раньше " + fmt(it) + ")" } ?: ""),
                color = theme.textSecondary, fontSize = 12.sp,
            )
            if (plan != null) {
                val v = series.firstOrNull()?.second?.getOrNull(i) ?: BigDecimal.ZERO
                val done = v.multiply(BigDecimal(100)).divide(plan.max(BigDecimal.ONE), 0, java.math.RoundingMode.HALF_UP)
                Text("$planLabel ${compactRub(plan)} · выполнено $done%", color = theme.textSecondary, fontSize = 12.sp)
            }
            val grid = theme.textSecondary.copy(alpha = 0.18f)
            val planColor = theme.textSecondary
            val guide = theme.textSecondary.copy(alpha = 0.45f)
            val panel = theme.panel
            Canvas(
                Modifier.fillMaxWidth().height(120.dp).padding(top = 6.dp)
                    .semantics { contentDescription = title }
                    .pointerInput(labels) {
                        detectTapGestures { p ->
                            selected = (p.x / (size.width.toFloat() / labels.size)).toInt().coerceIn(0, labels.lastIndex)
                        }
                    },
            ) {
                val slot = size.width / labels.size
                val top = 6.dp.toPx()
                val h = size.height - top
                fun y(v: BigDecimal) = top + h - (v.toFloat() / max.toFloat()) * h * reveal.value
                fun path(values: List<BigDecimal>): Pair<androidx.compose.ui.graphics.Path, List<Offset>> {
                    val pts = labels.indices.map { Offset(slot * (it + 0.5f), y(values.getOrNull(it) ?: BigDecimal.ZERO)) }
                    return androidx.compose.ui.graphics.Path().apply {
                        moveTo(pts[0].x, pts[0].y)
                        for (k in 1 until pts.size) {
                            val mid = (pts[k - 1].x + pts[k].x) / 2
                            cubicTo(mid, pts[k - 1].y, mid, pts[k].y, pts[k].x, pts[k].y)
                        }
                    } to pts
                }
                for (g in 0..2) {
                    val gy = top + h * g / 2f
                    drawLine(grid, Offset(0f, gy), Offset(size.width, gy), strokeWidth = 1.dp.toPx())
                }
                drawLine(guide, Offset(slot * (i + 0.5f), top), Offset(slot * (i + 0.5f), size.height), strokeWidth = 1.dp.toPx(),
                    pathEffect = PathEffect.dashPathEffect(floatArrayOf(6f, 6f)))
                // Прошлый период — бледный пунктир на фоне.
                previous.forEachIndexed { s, values ->
                    if (values == null || s >= colors.size) return@forEachIndexed
                    drawPath(path(values).first, colors[s].copy(alpha = 0.35f), style = androidx.compose.ui.graphics.drawscope.Stroke(
                        width = 1.5.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(8f, 8f)),
                    ))
                }
                series.forEachIndexed { s, (_, values) ->
                    val (line, pts) = path(values)
                    val fill = androidx.compose.ui.graphics.Path().apply {
                        addPath(line)
                        lineTo(pts.last().x, size.height)
                        lineTo(pts.first().x, size.height)
                        close()
                    }
                    val alpha = if (series.size > 1) 0.18f else 0.32f
                    drawPath(fill, androidx.compose.ui.graphics.Brush.verticalGradient(
                        listOf(colors[s].copy(alpha = alpha), colors[s].copy(alpha = 0f)), startY = top, endY = size.height,
                    ))
                    drawPath(line, colors[s], style = androidx.compose.ui.graphics.drawscope.Stroke(
                        width = 2.5.dp.toPx(), cap = androidx.compose.ui.graphics.StrokeCap.Round,
                        join = androidx.compose.ui.graphics.StrokeJoin.Round,
                    ))
                    drawCircle(panel, 5.dp.toPx(), pts[i])
                    drawCircle(colors[s], 3.5.dp.toPx(), pts[i])
                }
                if (plan != null) {
                    val py = y(plan)
                    drawLine(planColor, Offset(0f, py), Offset(size.width, py), strokeWidth = 1.5.dp.toPx(),
                        pathEffect = PathEffect.dashPathEffect(floatArrayOf(10f, 8f)))
                }
            }
            // Подписи: не больше 8, выбранная — всегда; рисуются на холсте, чтобы «30» не обрезалось в узком столбце.
            val step = ((labels.size + 7) / 8).coerceAtLeast(1)
            val labelColor = theme.textSecondary.toArgb()
            val activeColor = theme.textPrimary.toArgb()
            Canvas(Modifier.fillMaxWidth().height(16.dp)) {
                val slot = size.width / labels.size
                val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply {
                    textSize = 11.sp.toPx()
                    textAlign = android.graphics.Paint.Align.CENTER
                }
                labels.forEachIndexed { m, k ->
                    val near = kotlin.math.abs(m - i) in 1 until step
                    if (m != i && (m % step != 0 || near)) return@forEachIndexed
                    paint.color = if (m == i) activeColor else labelColor
                    paint.isFakeBoldText = m == i
                    val half = paint.measureText(k) / 2
                    val x = (slot * (m + 0.5f)).coerceIn(half, size.width - half)
                    drawContext.canvas.nativeCanvas.drawText(k, x, size.height - 3.dp.toPx(), paint)
                }
            }
        }
    }
}

/** Маленький переключатель-«таблетка»: ₽ | шт. */
@Composable
private fun Segmented(options: List<String>, selected: Int, onSelect: (Int) -> Unit) {
    val theme = LocalKnitColors.current
    Surface(shape = RoundedCornerShape(50), color = theme.background) {
        Row(Modifier.padding(2.dp)) {
            options.forEachIndexed { k, label ->
                val on = k == selected
                Surface(
                    onClick = { onSelect(k) }, shape = RoundedCornerShape(50),
                    color = if (on) theme.accent else Color.Transparent,
                ) {
                    Text(
                        label, color = if (on) theme.background else theme.textSecondary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp),
                    )
                }
            }
        }
    }
}
