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
            var form = new Form1();
            Boolean b = form.Initialize();

            if (b == false)
            {
                MessageBox.Show("无法启动Direct3D", "错误");
                return;
            }

            //如果初始化成功，则显示窗体
            form.Show();
            while (form.Created)
            {
                form.Render();
                Application.DoEvents();
            }
        }
    }
}
