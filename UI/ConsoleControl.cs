using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Console de log style Honorbuddy
    /// Affiche messages, warnings, erreurs
    /// </summary>
    public class ConsoleControl : RichTextBox
    {
        private const int MAX_LINES = 1000;
        private Queue<string> _lineBuffer = new Queue<string>();

        public ConsoleControl()
        {
            ReadOnly = true;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.FromArgb(220, 220, 220);
            Font = new Font("Consolas", 9);
            BorderStyle = BorderStyle.None;
            Height = 100;
            Dock = DockStyle.Bottom;
        }

        /// <summary>
        /// Log message normal
        /// </summary>
        public void Log(string message)
        {
            AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n", Color.White);
        }

        /// <summary>
        /// Log message de succès
        /// </summary>
        public void LogSuccess(string message)
        {
            AppendText($"[{DateTime.Now:HH:mm:ss}] ✓ {message}\n", Color.LimeGreen);
        }

        /// <summary>
        /// Log warning
        /// </summary>
        public void LogWarning(string message)
        {
            AppendText($"[{DateTime.Now:HH:mm:ss}] ⚠ {message}\n", Color.Orange);
        }

        /// <summary>
        /// Log erreur
        /// </summary>
        public void LogError(string message)
        {
            AppendText($"[{DateTime.Now:HH:mm:ss}] ✗ {message}\n", Color.Red);
        }

        private void AppendText(string text, Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendText(text, color)));
                return;
            }

            _lineBuffer.Enqueue(text);
            while (_lineBuffer.Count > MAX_LINES)
                _lineBuffer.Dequeue();

            SelectionStart = TextLength;
            SelectionLength = 0;
            SelectionColor = color;
            AppendText(text);
            SelectionColor = ForeColor;
            ScrollToCaret();
        }

        /// <summary>
        /// Efface la console
        /// </summary>
        public void ClearConsole()
        {
            Clear();
            _lineBuffer.Clear();
        }
    }
}
