using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KeyboardWorkerSetup
{
    // The one window of the setup program: a button, a progress bar and a line saying what is happening.
    public sealed class SetupForm : Form
    {
        readonly Label status = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 44, Text = "버튼을 누르면 GitHub에서 최신 버전을 받아 설치합니다. (약 80MB)" };
        readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Top, Height = 18, Maximum = 1000 };
        readonly CheckBox desktop = new CheckBox { Text = "바탕화면에 바로가기 만들기", Checked = true, Dock = DockStyle.Top, Height = 30 };
        readonly Button action = new Button { Text = "설치하기", Dock = DockStyle.Bottom, Height = 40 };
        readonly CancellationTokenSource closing = new CancellationTokenSource();
        readonly string folder = SetupCore.DefaultFolder;
        bool busy, done;
        public SetupForm()
        {
            Text = SetupCore.Title + " 설치"; Font = new Font("Malgun Gothic", 10f); ClientSize = new Size(440, 230); Padding = new Padding(18);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (ArgumentException) { }
            var title = new Label { Text = SetupCore.Title, Font = new Font("Malgun Gothic", 15f, FontStyle.Bold), Dock = DockStyle.Top, Height = 40 };
            var place = new Label { Text = "설치 위치: " + folder, Dock = DockStyle.Top, Height = 26, ForeColor = SystemColors.GrayText, AutoEllipsis = true, Font = new Font("Malgun Gothic", 8.5f) };
            // Docked controls stack in reverse order of adding.
            Controls.Add(action); Controls.Add(place); Controls.Add(desktop); Controls.Add(bar); Controls.Add(status); Controls.Add(title);
            action.Click += async (_, __) => await Act();
            FormClosing += (_, e) => { if (busy && MessageBox.Show(this, "설치를 그만둘까요?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true; else closing.Cancel(); };
        }
        async Task Act()
        {
            if (done) { Process.Start(new ProcessStartInfo(Path.Combine(folder, SetupCore.Program)) { WorkingDirectory = folder, UseShellExecute = true }); Close(); return; }
            busy = true; action.Enabled = desktop.Enabled = false; bar.Value = 0;
            try
            {
                string version = await SetupCore.Install(folder, true, desktop.Checked, text => status.Text = text,
                    (now, total) => { if (total > 0) { bar.Value = (int)Math.Min(1000, now * 1000 / total); status.Text = $"내려받는 중... {now / 1048576.0:0.0} / {total / 1048576.0:0.0} MB"; } }, closing.Token);
                bar.Value = 1000; done = true; status.Text = "설치가 끝났습니다." + (version.Length > 0 ? " (버전 " + version + ")" : "") + " 시작 메뉴에서도 찾을 수 있어요."; action.Text = "실행하기";
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is InvalidOperationException || e is HttpRequestException || e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                status.Text = e is HttpRequestException ? "인터넷 연결을 확인한 뒤 다시 눌러 주세요." : e is InvalidOperationException ? e.Message : "설치하지 못했습니다: " + e.Message; action.Text = "다시 시도";
            }
            finally { busy = false; action.Enabled = true; desktop.Enabled = !done; }
        }

        // "--silent <folder>" installs without a window or shortcuts and answers with an exit code; used to check the setup program itself.
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--silent")
            {
                try { SetupCore.Install(Path.GetFullPath(args[1]), false, false, null, null, CancellationToken.None).GetAwaiter().GetResult(); return 0; }
                catch (Exception e) { try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "KeyboardWorker-Setup-error.txt"), e.ToString()); } catch (IOException) { } return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm()); return 0;
        }
    }
}
