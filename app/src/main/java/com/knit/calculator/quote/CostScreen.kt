package com.knit.calculator.quote

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.CostCalculator
import com.knit.calculator.core.CostSettings
import com.knit.calculator.core.Product
import com.knit.calculator.core.ProductCost
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import java.math.BigDecimal
import java.math.RoundingMode

/** Поля калькулятора: всё редактируется, начальные значения — из листов «Себестоимость» и «Настройки». */
private data class CostInputs(
    val yarn: String = "",
    val minutes: String = "",
    val minutePrice: String = "",
    val handOps: String = "",
    val opPrice: String = "",
    val wto: String = "",
    val packaging: String = "",
    val waste: String = "",
    val fixed: String = "",
    val plan: String = "",
    val commission: String = "",
    val target: String = "",
    val quantity: String = "1000",
    val price: String = "",
)

private fun d(text: String): BigDecimal = YarnCalculator.parseDecimal(text) ?: BigDecimal.ZERO
private fun s(v: BigDecimal): String = v.setScale(2, RoundingMode.HALF_UP).stripTrailingZeros().toPlainString().replace('.', ',')
private fun rub(v: BigDecimal) = QuoteCalculator.formatMoney(v) + " ₽"

/**
 * Калькулятор себестоимости (директор): пряжа, работа, ВТО и упаковка, брак, доля постоянных расходов;
 * безубыточная и рекомендованная цена; прибыль при своей цене и «Создать КП с этой ценой».
 */
@Composable
fun CostScreen(viewModel: QuoteViewModel, onBack: () -> Unit, onOpenQuote: () -> Unit) {
    val colors = LocalKnitColors.current
    val catalog by viewModel.catalog.collectAsStateWithLifecycle()
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    var product by remember { mutableStateOf<Product?>(null) }
    var picker by remember { mutableStateOf(false) }
    val base = remember(settings) { settings.costSettings() }
    var f by remember(base) {
        mutableStateOf(
            CostInputs(
                fixed = s(base.fixedMonthly), plan = s(base.planQuantity),
                commission = s(base.commissionPercent), target = s(base.targetMarginPercent),
            ),
        )
    }
    var note by rememberSaveable { mutableStateOf<String?>(null) }
    val noCost = stringResource(R.string.cost_no_sheet)

    fun pick(p: Product) {
        product = p
        val c = viewModel.productCost(p)
        note = if (c == null) noCost else null
        f = f.copy(
            yarn = c?.let { s(viewModel.yarnPerUnit(p, it)) }.orEmpty(),
            minutes = c?.let { s(it.knitMinutes) }.orEmpty(), minutePrice = c?.let { s(it.minutePrice) }.orEmpty(),
            handOps = c?.let { s(it.handOperations) }.orEmpty(), opPrice = c?.let { s(it.operationPrice) }.orEmpty(),
            wto = c?.let { s(it.wto) }.orEmpty(), packaging = c?.let { s(it.packaging) }.orEmpty(),
            waste = c?.let { s(it.wastePercent) }.orEmpty(),
            quantity = p.minOrder.takeIf { it > BigDecimal.ONE }?.let(::s) ?: f.quantity,
            price = s(QuoteCalculator.unitPrice(p, emptyMap(), d(f.quantity).max(BigDecimal.ONE)).first),
        )
    }

    val cost = ProductCost(
        yarnPerUnit = d(f.yarn), knitMinutes = d(f.minutes), minutePrice = d(f.minutePrice),
        handOperations = d(f.handOps), operationPrice = d(f.opPrice), wto = d(f.wto), packaging = d(f.packaging), wastePercent = d(f.waste),
    )
    val cs = CostSettings(d(f.fixed), d(f.plan), d(f.commission), d(f.target), base.vat)
    val unit = CostCalculator.unitCost(cost, cs, d(f.yarn))
    val qty = d(f.quantity).max(BigDecimal.ONE)
    val price = d(f.price)
    val eco = CostCalculator.economics(unit, cs, price, qty)

    FormScreen(stringResource(R.string.cost_title), onBack) {
        Surface(onClick = { picker = true }, shape = RoundedCornerShape(16.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp)) {
                Text(stringResource(R.string.cost_product), color = colors.textSecondary, fontSize = 13.sp)
                Text(product?.name ?: stringResource(R.string.cost_product_pick), color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
            }
        }
        note?.let { Text(it, color = colors.textSecondary, fontSize = 13.sp) }

        // Итог — сверху, чтобы видеть результат при правке полей.
        Surface(shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(stringResource(R.string.cost_unit), color = colors.textSecondary, fontSize = 14.sp)
                Text(rub(unit.total), color = colors.textPrimary, fontSize = 26.sp, fontWeight = FontWeight.Bold)
                Text(
                    stringResource(R.string.cost_breakdown, rub(unit.yarn), rub(unit.labour), rub(unit.other), rub(unit.waste), rub(unit.fixed)),
                    color = colors.textSecondary, fontSize = 13.sp,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(R.string.cost_break_even), color = colors.textSecondary, fontSize = 13.sp)
                        Text(rub(eco.breakEvenPrice), color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
                    }
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(R.string.cost_target, f.target.ifBlank { "0" }), color = colors.textSecondary, fontSize = 13.sp)
                        Text(rub(eco.targetPrice), color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
                    }
                }
                if (price.signum() > 0) {
                    val loss = eco.unitProfit.signum() < 0
                    Text(
                        stringResource(R.string.cost_at_price, rub(price), rub(eco.unitProfit), eco.marginPercent.stripTrailingZeros().toPlainString().replace('.', ','), rub(eco.totalProfit)),
                        color = if (loss) Color(0xFFD32F2F) else colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold,
                    )
                }
                TextButton(onClick = { f = f.copy(price = s(eco.targetPrice)) }) {
                    Text(stringResource(R.string.cost_use_target), color = colors.textSecondary)
                }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KnitField(f.price, { f = f.copy(price = it) }, R.string.cost_price, suffix = "₽", modifier = Modifier.weight(1f))
            KnitField(f.quantity, { f = f.copy(quantity = it) }, R.string.cost_quantity, suffix = "шт", modifier = Modifier.weight(1f))
        }
        val p = product
        if (p != null && price.signum() > 0) {
            ActionButton(R.string.cost_make_quote, R.drawable.ic_quote, primary = true, Modifier.fillMaxWidth()) {
                viewModel.quoteAtPrice(p, qty, price)
                onOpenQuote()
            }
        }

        SectionTitle(R.string.cost_direct)
        KnitField(f.yarn, { f = f.copy(yarn = it) }, R.string.cost_yarn, suffix = "₽/шт")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KnitField(f.minutes, { f = f.copy(minutes = it) }, R.string.cost_minutes, suffix = "мин", modifier = Modifier.weight(1f))
            KnitField(f.minutePrice, { f = f.copy(minutePrice = it) }, R.string.cost_minute_price, suffix = "₽", modifier = Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KnitField(f.handOps, { f = f.copy(handOps = it) }, R.string.cost_hand_ops, modifier = Modifier.weight(1f))
            KnitField(f.opPrice, { f = f.copy(opPrice = it) }, R.string.cost_op_price, suffix = "₽", modifier = Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KnitField(f.wto, { f = f.copy(wto = it) }, R.string.cost_wto, suffix = "₽", modifier = Modifier.weight(1f))
            KnitField(f.packaging, { f = f.copy(packaging = it) }, R.string.cost_packaging, suffix = "₽", modifier = Modifier.weight(1f))
        }
        KnitField(f.waste, { f = f.copy(waste = it) }, R.string.cost_waste, suffix = "%")

        SectionTitle(R.string.cost_factory)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KnitField(f.fixed, { f = f.copy(fixed = it) }, R.string.cost_fixed, suffix = "₽", modifier = Modifier.weight(1f))
            KnitField(f.plan, { f = f.copy(plan = it) }, R.string.cost_plan, suffix = "шт", modifier = Modifier.weight(1f))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            KnitField(f.commission, { f = f.copy(commission = it) }, R.string.cost_commission, suffix = "%", modifier = Modifier.weight(1f))
            KnitField(f.target, { f = f.copy(target = it) }, R.string.cost_target_field, suffix = "%", modifier = Modifier.weight(1f))
        }
        Text(stringResource(R.string.cost_hint, base.vat.ratePercent.stripTrailingZeros().toPlainString()), color = colors.textSecondary, fontSize = 12.sp)
    }

    if (picker) {
        ProductPicker(
            calculator = catalog, msProducts = emptyList(), sync = SyncStatus(),
            onRefresh = {}, onPick = { picker = false; pick(it) }, onDismiss = { picker = false },
            title = stringResource(R.string.cost_product_pick),
        )
    }
}
