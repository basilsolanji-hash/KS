package com.knit.calculator.core

import java.math.BigDecimal
import java.math.MathContext
import java.math.RoundingMode

/** Символы, из которых состоит выражение калькулятора. */
object Symbols {
    const val PLUS = '+'
    const val MINUS = '−' // U+2212, отображаемый минус
    const val TIMES = '×'
    const val DIVIDE = '÷'
    const val PERCENT = '%'
    const val DOT = '.'
    const val LPAREN = '('
    const val RPAREN = ')'

    val OPERATORS = setOf(PLUS, MINUS, TIMES, DIVIDE)

    fun isOperator(c: Char) = c in OPERATORS
}

enum class CalcError {
    DIVISION_BY_ZERO,
    INVALID_EXPRESSION,
    OVERFLOW,
}

sealed interface EvalResult {
    data class Success(val value: BigDecimal) : EvalResult
    data class Failure(val error: CalcError) : EvalResult
}

/**
 * Безопасный вычислитель выражений (рекурсивный спуск, без выполнения кода).
 *
 * Правила:
 * - приоритет: скобки → унарный минус → `%` → `×`/`÷` → `+`/`−`;
 * - `a + b%` и `a − b%` означают «a плюс/минус b процентов от a»;
 * - в остальных случаях `b%` = b / 100 (например, `200 × 10%` = 20);
 * - незакрытые скобки в конце выражения закрываются автоматически;
 * - промежуточные значения считаются в [BigDecimal] с 34 значащими цифрами.
 */
object Evaluator {
    private val MC = MathContext(34, RoundingMode.HALF_EVEN)
    private val HUNDRED = BigDecimal(100)
    private val MAX_ABS = BigDecimal.ONE.scaleByPowerOfTen(1000)
    private val MIN_ABS = BigDecimal.ONE.scaleByPowerOfTen(-1000)

    fun evaluate(expression: String): EvalResult {
        if (expression.isBlank()) return EvalResult.Failure(CalcError.INVALID_EXPRESSION)
        return try {
            val node = Parser(expression).parse()
            EvalResult.Success(eval(node))
        } catch (e: CalcException) {
            EvalResult.Failure(e.error)
        } catch (e: ArithmeticException) {
            EvalResult.Failure(CalcError.INVALID_EXPRESSION)
        } catch (e: NumberFormatException) {
            EvalResult.Failure(CalcError.INVALID_EXPRESSION)
        }
    }

    private fun eval(node: Node): BigDecimal = when (node) {
        is Node.Num -> node.value
        is Node.Neg -> eval(node.operand).negate()
        is Node.Percent -> check(eval(node.operand).divide(HUNDRED, MC))
        is Node.Binary -> {
            val left = eval(node.left)
            val result = when (node.op) {
                Symbols.PLUS, Symbols.MINUS -> {
                    val right = if (node.right is Node.Percent) {
                        // a ± b%  →  a ± a·b/100
                        left.multiply(eval(node.right.operand), MC).divide(HUNDRED, MC)
                    } else {
                        eval(node.right)
                    }
                    if (node.op == Symbols.PLUS) left.add(right, MC) else left.subtract(right, MC)
                }
                Symbols.TIMES -> left.multiply(eval(node.right), MC)
                Symbols.DIVIDE -> {
                    val right = eval(node.right)
                    if (right.signum() == 0) throw CalcException(CalcError.DIVISION_BY_ZERO)
                    left.divide(right, MC)
                }
                else -> throw CalcException(CalcError.INVALID_EXPRESSION)
            }
            check(result)
        }
    }

    private fun check(value: BigDecimal): BigDecimal {
        val abs = value.abs()
        if (abs >= MAX_ABS) throw CalcException(CalcError.OVERFLOW)
        if (value.signum() != 0 && abs < MIN_ABS) return BigDecimal.ZERO
        return value
    }

    private class CalcException(val error: CalcError) : Exception()

    private sealed interface Node {
        data class Num(val value: BigDecimal) : Node
        data class Neg(val operand: Node) : Node
        data class Percent(val operand: Node) : Node
        data class Binary(val op: Char, val left: Node, val right: Node) : Node
    }

    private class Parser(private val s: String) {
        private var pos = 0
        private var depth = 0

        fun parse(): Node {
            val node = parseExpression()
            if (pos != s.length) invalid()
            return node
        }

        private fun peek(): Char? = s.getOrNull(pos)

        private fun parseExpression(): Node {
            var left = parseTerm()
            while (true) {
                val op = when (peek()) {
                    '+' -> Symbols.PLUS
                    Symbols.MINUS, '-' -> Symbols.MINUS
                    else -> return left
                }
                pos++
                left = Node.Binary(op, left, parseTerm())
            }
        }

        private fun parseTerm(): Node {
            var left = parseUnary()
            while (true) {
                val op = when (peek()) {
                    Symbols.TIMES, '*' -> Symbols.TIMES
                    Symbols.DIVIDE, '/' -> Symbols.DIVIDE
                    else -> return left
                }
                pos++
                left = Node.Binary(op, left, parseUnary())
            }
        }

        private fun parseUnary(): Node = when (peek()) {
            Symbols.MINUS, '-' -> {
                pos++
                Node.Neg(parseUnary())
            }
            '+' -> {
                pos++
                parseUnary()
            }
            else -> parsePostfix()
        }

        private fun parsePostfix(): Node {
            var node = parsePrimary()
            while (peek() == Symbols.PERCENT) {
                pos++
                node = Node.Percent(node)
            }
            return node
        }

        private fun parsePrimary(): Node {
            val c = peek() ?: invalid()
            if (c == Symbols.LPAREN) {
                pos++
                if (++depth > MAX_DEPTH) invalid()
                val inner = parseExpression()
                depth--
                when {
                    peek() == Symbols.RPAREN -> pos++
                    pos == s.length -> Unit // незакрытая скобка в конце — закрываем автоматически
                    else -> invalid()
                }
                return inner
            }
            if (c.isDigit() || c == Symbols.DOT) return parseNumber()
            invalid()
        }

        private fun parseNumber(): Node {
            val start = pos
            while (peek()?.let { it.isDigit() || it == Symbols.DOT } == true) pos++
            if (peek() == 'E') {
                pos++
                if (peek() == '-' || peek() == '+') pos++
                val expStart = pos
                while (peek()?.isDigit() == true) pos++
                if (pos == expStart) invalid()
            }
            val text = s.substring(start, pos)
            if (text == ".") invalid()
            return Node.Num(BigDecimal(text))
        }

        private fun invalid(): Nothing = throw CalcException(CalcError.INVALID_EXPRESSION)

        private companion object {
            const val MAX_DEPTH = 100
        }
    }
}
