using System;
using System.Windows.Forms;

namespace MeshViewer3D
{
    /// <summary>
    /// Point d'entrée principal de MeshViewer3D
    /// Outil professionnel de visualisation de navmesh WoW 3.3.5a
    /// Qualité Honorbuddy/Apoc - Zero spaghetti code
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UI.MainForm());
        }
    }
}
