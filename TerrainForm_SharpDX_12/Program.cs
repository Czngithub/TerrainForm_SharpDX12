using System;
using System.Windows.Forms;

namespace TerrainForm_SharpDX_12
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            var form = new SharpDX.Windows.RenderForm("地形")
            {
                Width = 1280,
                Height = 800

            };
            var app = new Form1();

            Boolean b = app.Initialize(form);
            if (b == false)
            {
                MessageBox.Show("无法启动Direct3D", "错误");
                return;
            }

            //如果初始化成功，则显示窗体
            form.Show();
            while (form.Created)
            {
                app.Render();
                Application.DoEvents();
            }
        }
    }
}
