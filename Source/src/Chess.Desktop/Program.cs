using System;
using Chess.Desktop;

namespace Chess.Desktop
{
    /// <summary>Desktop entry point. One shell, one window; everything else lives
    /// in ChessGame so the same shell sources can be shared with the mobile
    /// platforms, which only swap this bootstrap class.</summary>
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            using (var game = new ChessGame())
                game.Run();
        }
    }
}
