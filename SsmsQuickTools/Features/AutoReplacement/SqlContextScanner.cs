using System.Collections.Generic;

namespace SsmsQuickTools.Features.AutoReplacement
{
    /// <summary>
    /// Determina si el cursor esta dentro de una cadena, un identificador entre corchetes/comillas
    /// o un comentario T-SQL, escaneando el texto desde el inicio del buffer hasta el cursor.
    /// Logica pura: no depende del clasificador interno de SSMS (no expuesto).
    /// </summary>
    public static class SqlContextScanner
    {
        private enum State
        {
            Code,
            SingleQuoteString,
            BracketIdentifier,
            DoubleQuoteIdentifier,
            LineComment,
            BlockComment,
        }

        public static bool IsInsideLiteralOrComment(string textUpToCaret)
        {
            if (string.IsNullOrEmpty(textUpToCaret))
            {
                return false;
            }

            var state = State.Code;
            var blockCommentDepth = 0;
            var i = 0;
            var length = textUpToCaret.Length;

            while (i < length)
            {
                var c = textUpToCaret[i];

                switch (state)
                {
                    case State.Code:
                        if (c == '\'')
                        {
                            state = State.SingleQuoteString;
                            i++;
                        }
                        else if (c == '[')
                        {
                            state = State.BracketIdentifier;
                            i++;
                        }
                        else if (c == '"')
                        {
                            state = State.DoubleQuoteIdentifier;
                            i++;
                        }
                        else if (c == '-' && Peek(textUpToCaret, i + 1) == '-')
                        {
                            state = State.LineComment;
                            i += 2;
                        }
                        else if (c == '/' && Peek(textUpToCaret, i + 1) == '*')
                        {
                            state = State.BlockComment;
                            blockCommentDepth = 1;
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                        break;

                    case State.SingleQuoteString:
                        if (c == '\'')
                        {
                            if (Peek(textUpToCaret, i + 1) == '\'')
                            {
                                i += 2; // '' escapada: sigue dentro de la cadena
                            }
                            else
                            {
                                state = State.Code;
                                i++;
                            }
                        }
                        else
                        {
                            i++;
                        }
                        break;

                    case State.BracketIdentifier:
                        if (c == ']')
                        {
                            state = State.Code;
                        }
                        i++;
                        break;

                    case State.DoubleQuoteIdentifier:
                        if (c == '"')
                        {
                            state = State.Code;
                        }
                        i++;
                        break;

                    case State.LineComment:
                        if (c == '\n')
                        {
                            state = State.Code;
                        }
                        i++;
                        break;

                    case State.BlockComment:
                        if (c == '/' && Peek(textUpToCaret, i + 1) == '*')
                        {
                            blockCommentDepth++;
                            i += 2;
                        }
                        else if (c == '*' && Peek(textUpToCaret, i + 1) == '/')
                        {
                            blockCommentDepth--;
                            i += 2;
                            if (blockCommentDepth == 0)
                            {
                                state = State.Code;
                            }
                        }
                        else
                        {
                            i++;
                        }
                        break;
                }
            }

            return state != State.Code;
        }

        private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';
    }
}
