package com.knit.calculator.core

/**
 * Состояние калькулятора. Хранится только сырое выражение; всё остальное вычисляется.
 *
 * @property expression текущее выражение в сыром виде (см. [NumberFormatter]).
 * @property preview предварительный результат, пока пользователь набирает выражение.
 * @property error ошибка последнего нажатия «=».
 * @property evaluated `true`, если [expression] — результат только что нажатого «=».
 * @property previousExpression выражение, давшее текущий результат (показывается над ним).
 */
data class CalculatorState(
    val expression: String = "",
    val preview: String? = null,
    val error: CalcError? = null,
    val evaluated: Boolean = false,
    val previousExpression: String? = null,
)

/** Запись истории вычислений. */
data class HistoryEntry(
    val expression: String,
    val result: String,
    val timestamp: Long,
)

/** Результат нажатия «=»: новое состояние и, если вычисление удалось, запись для истории. */
data class EvaluationOutcome(val state: CalculatorState, val historyEntry: HistoryEntry?)

/**
 * Редактор выражения: чистые функции «состояние → состояние».
 * Каждая функция защищена от некорректной последовательности нажатий —
 * недопустимое нажатие просто игнорируется.
 */
object CalculatorEngine {
    const val MAX_LENGTH = 120
    const val MAX_DIGITS_PER_NUMBER = NumberFormatter.SIGNIFICANT_DIGITS
    private const val MAX_OPEN_PARENS = 20

    private const val MINUS = Symbols.MINUS

    fun digit(state: CalculatorState, d: Char): CalculatorState {
        require(d in '0'..'9')
        if (state.evaluated) return edited(d.toString())
        val expr = state.expression
        if (expr.length >= MAX_LENGTH) return state
        val last = expr.lastOrNull()
        if (last == Symbols.RPAREN || last == Symbols.PERCENT) return edited(expr + Symbols.TIMES + d)
        val number = trailingNumber(expr)
        if (number != null) {
            if ('E' in number) return state
            if (number == "0") {
                return if (d == '0') state else edited(expr.dropLast(1) + d)
            }
            if (number.count { it.isDigit() } >= MAX_DIGITS_PER_NUMBER) return state
        }
        return edited(expr + d)
    }

    fun dot(state: CalculatorState): CalculatorState {
        if (state.evaluated) return edited("0.")
        val expr = state.expression
        if (expr.length >= MAX_LENGTH) return state
        val number = trailingNumber(expr)
        return when {
            number != null && (Symbols.DOT in number || 'E' in number) -> state
            number != null -> edited(expr + Symbols.DOT)
            expr.lastOrNull() == Symbols.RPAREN || expr.lastOrNull() == Symbols.PERCENT ->
                edited(expr + Symbols.TIMES + "0.")
            else -> edited(expr + "0.")
        }
    }

    fun operator(state: CalculatorState, op: Char): CalculatorState {
        require(Symbols.isOperator(op))
        val expr = state.expression
        if (expr.length >= MAX_LENGTH) return state
        val last = expr.lastOrNull() ?: return if (op == MINUS) edited(MINUS.toString()) else state
        val prev = expr.getOrNull(expr.length - 2)
        return when {
            Symbols.isOperator(last) -> when {
                // Одинокий унарный минус в начале или после «(»: его можно только оставить.
                prev == null || prev == Symbols.LPAREN -> if (op == MINUS) edited(expr) else state
                // «5×−» → заменяем пару целиком.
                last == MINUS && (prev == Symbols.TIMES || prev == Symbols.DIVIDE) ->
                    if (op == MINUS) edited(expr) else edited(expr.dropLast(2) + op)
                // «5×» + «−» → унарный минус для следующего числа.
                op == MINUS && (last == Symbols.TIMES || last == Symbols.DIVIDE) -> edited(expr + op)
                else -> edited(expr.dropLast(1) + op)
            }
            last == Symbols.LPAREN -> if (op == MINUS) edited(expr + op) else state
            else -> edited(expr + op)
        }
    }

    fun percent(state: CalculatorState): CalculatorState {
        val expr = state.expression
        if (expr.length >= MAX_LENGTH) return state
        val last = expr.lastOrNull() ?: return state
        return if (last.isDigit() || last == Symbols.DOT || last == Symbols.RPAREN) edited(expr + Symbols.PERCENT) else state
    }

    fun openParen(state: CalculatorState): CalculatorState {
        val expr = if (state.evaluated) "" else state.expression
        if (expr.length >= MAX_LENGTH || openParens(expr) >= MAX_OPEN_PARENS) return state
        return if (endsWithValue(expr)) edited(expr + Symbols.TIMES + Symbols.LPAREN) else edited(expr + Symbols.LPAREN)
    }

    fun closeParen(state: CalculatorState): CalculatorState {
        if (state.evaluated) return state
        val expr = state.expression
        if (expr.length >= MAX_LENGTH) return state
        return if (openParens(expr) > 0 && endsWithValue(expr)) edited(expr + Symbols.RPAREN) else state
    }

    /** Умная кнопка «( )»: закрывает скобку, если это уместно, иначе открывает новую. */
    fun parentheses(state: CalculatorState): CalculatorState {
        val expr = state.expression
        return if (!state.evaluated && openParens(expr) > 0 && endsWithValue(expr)) closeParen(state) else openParen(state)
    }

    /** Смена знака последнего числа: `5+3` → `5+(−3` → `5+3`. */
    fun toggleSign(state: CalculatorState): CalculatorState {
        val expr = state.expression
        val last = expr.lastOrNull() ?: return edited("${Symbols.LPAREN}$MINUS")
        if (last == Symbols.RPAREN || last == Symbols.PERCENT) return state
        val number = trailingNumber(expr)
        if (number == null) {
            val prev = expr.getOrNull(expr.length - 2)
            return when {
                // Уже стоит унарный минус без числа — убираем его.
                last == MINUS && (prev == null || prev == Symbols.LPAREN) -> edited(expr.dropLast(1))
                Symbols.isOperator(last) || last == Symbols.LPAREN -> {
                    if (expr.length + 2 > MAX_LENGTH) state else edited(expr + Symbols.LPAREN + MINUS)
                }
                else -> state
            }
        }
        val start = expr.length - number.length
        val head = expr.substring(0, start)
        return when {
            head.endsWith("${Symbols.LPAREN}$MINUS") -> edited(head.dropLast(2) + number)
            head == MINUS.toString() -> edited(number)
            head.endsWith(MINUS) && head.length >= 2 &&
                (head[head.length - 2] == Symbols.TIMES || head[head.length - 2] == Symbols.DIVIDE) ->
                edited(head.dropLast(1) + number)
            expr.length + 2 > MAX_LENGTH -> state
            else -> edited(head + Symbols.LPAREN + MINUS + number)
        }
    }

    fun backspace(state: CalculatorState): CalculatorState {
        val expr = state.expression
        if (expr.isEmpty()) return state
        // После «=» стираем результат целиком, если он в экспоненциальной записи.
        if (state.evaluated && 'E' in expr) return CalculatorState()
        var cut = expr.dropLast(1)
        // Не оставляем «висящую» экспоненту вроде «1.5E» или «1.5E-».
        cut = cut.trimEnd('E', '-')
        return edited(cut)
    }

    fun clear(): CalculatorState = CalculatorState()

    fun evaluate(state: CalculatorState, now: Long = System.currentTimeMillis()): EvaluationOutcome {
        if (state.evaluated) return EvaluationOutcome(state, null)
        val expr = trimIncomplete(state.expression)
        if (expr.isEmpty()) return EvaluationOutcome(state, null)
        return when (val result = Evaluator.evaluate(expr)) {
            is EvalResult.Failure -> EvaluationOutcome(state.copy(error = result.error, preview = null), null)
            is EvalResult.Success -> {
                val raw = NumberFormatter.toRaw(result.value)
                val closed = expr + Symbols.RPAREN.toString().repeat(openParens(expr).coerceAtLeast(0))
                val isTrivial = !hasOperation(expr)
                EvaluationOutcome(
                    CalculatorState(
                        expression = raw,
                        evaluated = true,
                        previousExpression = if (isTrivial) null else closed,
                    ),
                    if (isTrivial) null else HistoryEntry(closed, raw, now),
                )
            }
        }
    }

    /** Восстанавливает запись истории как только что вычисленный результат. */
    fun restore(entry: HistoryEntry): CalculatorState =
        CalculatorState(expression = entry.result, evaluated = true, previousExpression = entry.expression)

    /** Подставляет сырое выражение (например, восстановленное после пересоздания экрана). */
    fun fromExpression(expression: String, evaluated: Boolean, previousExpression: String?): CalculatorState =
        if (evaluated) CalculatorState(expression, evaluated = true, previousExpression = previousExpression)
        else edited(expression)

    // ---------------------------------------------------------------------

    private fun edited(expr: String) = CalculatorState(expression = expr, preview = previewFor(expr))


    private fun previewFor(expr: String): String? {
        val trimmed = trimIncomplete(expr)
        if (!hasOperation(trimmed)) return null
        val result = Evaluator.evaluate(trimmed) as? EvalResult.Success ?: return null
        return NumberFormatter.toRaw(result.value)
    }

    /** Отбрасывает незавершённый хвост: операторы, «(» и одиночный унарный минус. */
    internal fun trimIncomplete(expr: String): String {
        var end = expr.length
        while (end > 0 && (Symbols.isOperator(expr[end - 1]) || expr[end - 1] == Symbols.LPAREN)) end--
        return expr.substring(0, end)
    }

    private fun hasOperation(expr: String): Boolean {
        val body = expr.removePrefix(MINUS.toString())
        return body.any { Symbols.isOperator(it) || it == Symbols.PERCENT || it == Symbols.LPAREN }
    }

    internal fun openParens(expr: String): Int =
        expr.count { it == Symbols.LPAREN } - expr.count { it == Symbols.RPAREN }

    private fun endsWithValue(expr: String): Boolean {
        val last = expr.lastOrNull() ?: return false
        return last.isDigit() || last == Symbols.DOT || last == Symbols.RPAREN || last == Symbols.PERCENT
    }

    /** Последнее число в конце выражения (цифры, точка, экспонента) или `null`. */
    internal fun trailingNumber(expr: String): String? {
        var i = expr.length
        while (i > 0) {
            val c = expr[i - 1]
            val partOfNumber = c.isDigit() || c == Symbols.DOT || c == 'E' ||
                (c == '-' && i >= 2 && expr[i - 2] == 'E')
            if (!partOfNumber) break
            i--
        }
        return if (i == expr.length) null else expr.substring(i)
    }
}
