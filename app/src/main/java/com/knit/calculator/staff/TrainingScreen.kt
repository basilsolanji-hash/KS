package com.knit.calculator.staff

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.ui.theme.LocalKnitColors

private data class Guide(val title: String, val intro: String, val steps: List<String>)

private val commonGuides = listOf(
    Guide(
        "Начало смены",
        "Отмечайте рабочее время на главном экране приложения.",
        listOf("Откройте главный экран.", "Нажмите «Начать смену» в карточке сотрудника.", "После окончания работы нажмите «Завершить смену»."),
    ),
    Guide(
        "Мои задачи",
        "Задачи помогают видеть поручения, сроки и договорённости в одном месте.",
        listOf("Откройте меню ☰ → «Задачи».", "Выберите задачу и прочитайте срок, чек-лист и комментарии.", "Переведите её в работу; после выполнения отметьте «Сделано» и дождитесь приёмки, если она требуется."),
    ),
    Guide(
        "Профиль и выплаты",
        "Проверьте личные данные и смотрите доступную вам информацию о начислениях.",
        listOf("Откройте меню ☰ → «Профиль».", "Заполните данные и отправьте профиль на подтверждение руководителю.", "Раздел «Выплаты» показывает начисления и историю, если руководитель открыл этот доступ."),
    ),
)

private val roleGuides = mapOf(
    "director" to listOf(
        Guide("Заказы МойСклад", "Работайте с заказами и связанными документами из одного списка.", listOf("Откройте меню ☰ → «Заказы МойСклад».", "Найдите заказ по номеру или клиенту.", "Откройте карточку, проверьте позиции и статус; перед проведением документа перепроверьте его данные.")),
        Guide("Контроль команды", "Смотрите смены, задачи и результаты сотрудников.", listOf("Откройте «Активность» для времени в приложении и журнала действий.", "Откройте «Рейтинг» для показателей за месяц.", "Назначайте задачи с исполнителем и сроком, затем проверяйте выполнение.")),
        Guide("Финансы и выплаты", "Финансовые данные доступны только руководителю.", listOf("Откройте меню ☰ → «Финансы».", "Сверьте поступления и расходы с МойСклад.", "Проверьте расчёт сотрудника в «Выплатах» перед внесением начислений.")),
    ),
    "assistant" to listOf(
        Guide("Поручения и контроль", "Помогайте руководителю держать задачи и сроки под контролем.", listOf("В «Задачах» назначайте поручения сотрудникам и указывайте срок.", "Добавляйте чек-лист и важные файлы к задаче.", "Проверяйте комментарии и просроченные задачи; завершение подтверждайте после результата.")),
        Guide("Сотрудники", "Используйте рабочие разделы команды в пределах назначенных прав.", listOf("Откройте «Сотрудники» или «Активность» из меню.", "Проверьте график смен и актуальность профиля.", "Для исправления закрытой смены или кадровых данных обратитесь к директору.")),
    ),
    "designer" to listOf(
        Guide("Производственное задание", "Перед началом работ сверьте описание заказа и требования к изделию.", listOf("Откройте раздел производства из меню.", "Найдите нужный заказ и проверьте количество, цвет, срок и технические примечания.", "Если данных не хватает, оставьте комментарий в задаче и дождитесь уточнения до передачи задания дальше.")),
        Guide("Передача результата", "Фиксируйте выполненную работу по фактическому результату.", listOf("Откройте нужный этап заказа.", "Укажите фактическое количество; брак сопровождайте причиной.", "Проверьте, что этап отображается следующим ответственным сотрудникам.")),
    ),
    "operator" to listOf(
        Guide("Работа на этапе", "Отмечайте только фактически выполненное количество.", listOf("Откройте раздел «Производство» и выберите назначенный заказ.", "Сверьте модель, цвет, количество и требования.", "Завершите этап с фактическим количеством; при браке укажите причину.")),
        Guide("Проблема или простой", "Сразу сообщайте о препятствиях, чтобы срок заказа можно было скорректировать.", listOf("Создайте задачу руководителю или технологу.", "Укажите номер заказа, этап, что остановило работу и когда это началось.", "После решения оставьте комментарий и продолжите работу по актуальному заданию.")),
    ),
    "handwork" to listOf(
        Guide("Ручная операция", "Перед началом проверьте комплектность и требования к заказу.", listOf("Откройте назначенный заказ в разделе производства.", "Сверьте количество и описание ручной операции.", "После выполнения внесите фактическое количество и отметьте выявленный брак с причиной.")),
    ),
    "manager" to listOf(
        Guide("Коммерческое предложение", "Подготовьте предложение и проверьте его перед отправкой клиенту.", listOf("Откройте «Новое КП» на главном экране.", "Выберите клиента из списка и проверьте реквизиты.", "Добавьте товары, количество и характеристики; перед отправкой проверьте цену, срок и итог.")),
        Guide("Связь с клиентом", "Сохраняйте следующий шаг после каждого контакта.", listOf("Откройте историю КП и найдите предложение клиента.", "Проверьте статус, дату и комментарии.", "Создайте задачу с датой повторного контакта, если клиенту нужно перезвонить или подготовить образец.")),
    ),
    "accountant" to listOf(
        Guide("Платежи и документы", "Сверяйте оплату с заказом и документом МойСклад.", listOf("Откройте «Оплаты» или «Заказы МойСклад».", "Найдите нужный заказ по номеру, клиенту или дате.", "Перед изменением или проведением документа проверьте сумму, контрагента и назначение платежа.")),
        Guide("Финансовый отчёт", "Используйте период отчёта, который нужен для сверки.", listOf("Откройте «Финансы».", "Выберите нужный период.", "Сверьте итоговые суммы с первичными документами в МойСклад.")),
    ),
    "merch" to listOf(
        Guide("Товары и остатки", "Проверяйте остаток перед подбором, маркировкой и складской операцией.", listOf("Откройте «Товары» и найдите товар по названию или штрихкоду.", "Сверьте модификацию, цвет, размер и склад «Электросталь».", "При расхождении обновите данные и сообщите руководителю до проведения операции.")),
        Guide("Этикетки и склад", "Этикетка должна соответствовать выбранной карточке товара.", listOf("Откройте «Этикетки», выберите товар или модификацию.", "Проверьте артикул, характеристики и EAN-13 в предпросмотре.", "Сверьте выбранный Bluetooth-принтер и количество копий перед печатью.")),
    ),
)

@Composable
fun TrainingScreen(staffRole: String?, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val roleTitle = when (staffRole) {
        "director" -> "Руководитель"
        "assistant" -> "Помощник директора"
        "designer" -> "Технолог / дизайнер"
        "operator" -> "Оператор"
        "handwork" -> "Ручные операции"
        "accountant" -> "Бухгалтер"
        "merch" -> "Товаровед"
        else -> "Сотрудник"
    }
    val guides = commonGuides + (roleGuides[staffRole] ?: emptyList())

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(colors.background)
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 18.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
            TextButton(onClick = onBack) { Text("‹ Назад", color = colors.accent) }
            Text("Обучение", color = colors.textPrimary, fontSize = 21.sp, fontWeight = FontWeight.Bold)
        }
        Text(
            "Инструкции для роли: $roleTitle",
            color = colors.textSecondary,
            fontSize = 14.sp,
        )
        Text(
            "Выберите тему и выполняйте шаги по порядку. Доступные разделы зависят от ваших прав.",
            color = colors.textSecondary,
            fontSize = 13.sp,
        )
        guides.forEach { guide ->
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(containerColor = colors.panel),
            ) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(9.dp),
                ) {
                    Text(guide.title, color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
                    Text(guide.intro, color = colors.textSecondary, fontSize = 14.sp)
                    guide.steps.forEachIndexed { index, step ->
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
                            Text("${index + 1}.", color = colors.accent, fontWeight = FontWeight.Bold)
                            Text(step, color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f))
                        }
                    }
                }
            }
        }
    }
}
