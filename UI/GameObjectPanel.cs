// ============================================================================
//  GameObjectPanel.cs  —  Phase 5: WMO/M2 object list panel
//
//  Displays unique WMO and M2 filenames from the loaded ADT tile.
//  Provides visibility toggles for each category.
//  Events are wired to NavMeshRenderer in MainForm — not here.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MeshViewer3D.Core.Formats.Adt;

namespace MeshViewer3D.UI
{
    /// <summary>
    /// Panel listing WMO and M2 objects loaded from an ADT tile.
    /// Mirrors the Honorbuddy Tripper.Renderer object list.
    /// </summary>
    public class GameObjectPanel : UserControl
    {
        // ── Controls ──────────────────────────────────────────────────────────
        private CheckBox _chkShowWmo = null!;
        private CheckBox _chkShowM2  = null!;
        private Label    _lblWmoCount = null!;
        private Label    _lblM2Count  = null!;
        private ListBox  _listWmo = null!;
        private ListBox  _listM2  = null!;

        // ── Events ────────────────────────────────────────────────────────────
        /// <summary>Fired when the "Show WMO" checkbox changes.</summary>
        public event Action<bool>? WmoVisibilityChanged;

        /// <summary>Fired when the "Show M2" checkbox changes.</summary>
        public event Action<bool>? M2VisibilityChanged;

        // ── Constructor ───────────────────────────────────────────────────────

        public GameObjectPanel()
        {
            this.BackColor = Color.FromArgb(37, 37, 38);
            this.Dock      = DockStyle.Fill;
            SetupUI();
        }

        // ── UI setup ──────────────────────────────────────────────────────────

        private void SetupUI()
        {
            int y = 10;

            // ── WMO section ───────────────────────────────────────────────────
            var lblWmo = new Label
            {
                Text      = "WMO Objects",
                Location  = new Point(10, y),
                AutoSize  = true,
                ForeColor = Color.White,
                Font      = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblWmo);

            _lblWmoCount = new Label
            {
                Text      = "(0)",
                Location  = new Point(160, y),
                AutoSize  = true,
                ForeColor = Color.Gray
            };
            Controls.Add(_lblWmoCount);
            y += 22;

            _chkShowWmo = new CheckBox
            {
                Text      = "Show WMO",
                Location  = new Point(10, y),
                Size      = new Size(215, 20),
                ForeColor = Color.LightGray,
                Checked   = true
            };
            _chkShowWmo.CheckedChanged += (_, _) => WmoVisibilityChanged?.Invoke(_chkShowWmo.Checked);
            Controls.Add(_chkShowWmo);
            y += 26;

            _listWmo = new ListBox
            {
                Location      = new Point(10, y),
                Size          = new Size(215, 110),
                BackColor     = Color.FromArgb(30, 30, 30),
                ForeColor     = Color.White,
                BorderStyle   = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.One,
                IntegralHeight = false
            };
            Controls.Add(_listWmo);
            y += 118;

            // ── Separator ─────────────────────────────────────────────────────
            var sep = new Label
            {
                Location  = new Point(10, y),
                Size      = new Size(215, 1),
                BackColor = Color.FromArgb(65, 65, 65)
            };
            Controls.Add(sep);
            y += 10;

            // ── M2 section ────────────────────────────────────────────────────
            var lblM2 = new Label
            {
                Text      = "M2 Objects",
                Location  = new Point(10, y),
                AutoSize  = true,
                ForeColor = Color.White,
                Font      = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblM2);

            _lblM2Count = new Label
            {
                Text      = "(0)",
                Location  = new Point(160, y),
                AutoSize  = true,
                ForeColor = Color.Gray
            };
            Controls.Add(_lblM2Count);
            y += 22;

            _chkShowM2 = new CheckBox
            {
                Text      = "Show M2",
                Location  = new Point(10, y),
                Size      = new Size(215, 20),
                ForeColor = Color.LightGray,
                Checked   = true
            };
            _chkShowM2.CheckedChanged += (_, _) => M2VisibilityChanged?.Invoke(_chkShowM2.Checked);
            Controls.Add(_chkShowM2);
            y += 26;

            _listM2 = new ListBox
            {
                Location      = new Point(10, y),
                Size          = new Size(215, 110),
                BackColor     = Color.FromArgb(30, 30, 30),
                ForeColor     = Color.White,
                BorderStyle   = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.One,
                IntegralHeight = false
            };
            Controls.Add(_listM2);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Populates the panel from a parsed ADT tile.
        /// Uses unique filenames (WmoNames/M2Names arrays) — not raw instance counts.
        /// Displays filename only (no directory path), e.g. "Stormwind.wmo".
        /// </summary>
        public void LoadObjects(AdtFile adt)
        {
            if (adt == null) { Clear(); return; }

            _listWmo.BeginUpdate();
            _listWmo.Items.Clear();
            var wmoNames = adt.WmoNames
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => Path.GetFileName(n))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n);
            foreach (var name in wmoNames) _listWmo.Items.Add(name);
            _listWmo.EndUpdate();
            _lblWmoCount.Text = $"({_listWmo.Items.Count})";

            _listM2.BeginUpdate();
            _listM2.Items.Clear();
            var m2Names = adt.M2Names
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => Path.GetFileName(n))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n);
            foreach (var name in m2Names) _listM2.Items.Add(name);
            _listM2.EndUpdate();
            _lblM2Count.Text = $"({_listM2.Items.Count})";
        }

        /// <summary>Clears all displayed objects and resets counts.</summary>
        public void Clear()
        {
            _listWmo.Items.Clear();
            _listM2.Items.Clear();
            _lblWmoCount.Text = "(0)";
            _lblM2Count.Text  = "(0)";
        }
    }
}
