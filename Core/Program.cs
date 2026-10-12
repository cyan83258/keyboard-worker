using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace KeyboardWorker;

public static class Program
{
    // --theme <id> shows one run in another theme without changing the saved setting (used to check screenshots).
    public static string? ThemeOverride { get; private set; }
    [STAThread] public static void Main(string[] args)
    {
        int themeAt = Array.IndexOf(args, "--theme"); if (themeAt >= 0 && themeAt + 1 < args.Length) ThemeOverride = args[themeAt + 1];
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write("처리되지 않은 오류 (프로그램 종료)", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write("백그라운드 작업 오류", e.Exception); e.SetObserved(); };
        StartupTrace.Begin();
        bool duoTest = args.Contains("--duo-integration-test"), smokeTest = args.Contains("--smoke-test") || args.Contains("--showcase-test");
        Pastel.Unattended = duoTest || smokeTest;

        // One-shot commands (no window): see Maintenance.
        if (Maintenance.TryRun(args)) return;
        Maintenance.PrepareSelfCheck(args);

        using var mutex = new Mutex(true, duoTest ? "Local\\KeyboardWorker.DuoTest" : Maintenance.InstanceName, out bool first);
        // A restart (after staging a settings import) waits for the previous instance to finish saving and exit.
        if (!first && args.Contains("--restart")) { try { first = mutex.WaitOne(20000); } catch (AbandonedMutexException) { first = true; } }
        if (!first) { MessageBox.Show("이미 실행 중입니다. 시스템 트레이의 Keyboard Worker 아이콘을 확인하세요."); return; }
        if (FactoryReset.ApplyPending() is string resetError) { ErrorLog.Write("전체 초기화 실패", resetError); MessageBox.Show("일부 파일을 지우지 못했습니다.\n" + resetError, "Keyboard Worker"); }
        if (SettingsBackup.ApplyPending() is string importError) { ErrorLog.Write("설정 가져오기 실패", importError); MessageBox.Show("설정 백업을 가져오지 못해 기존 설정으로 시작합니다.\n" + importError, "Keyboard Worker"); }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { ErrorLog.Write("작업 중 오류", e.Exception); MessageBox.Show("작업 중 오류가 발생했습니다.\n" + e.Exception.Message + "\n\n자세한 내용은 설정 → 시스템 → 오류 기록 보기에서 확인할 수 있어요.", "Keyboard Worker"); e.Handled = true; };
        OrderedStore.Failed += message => { ErrorLog.Write("저장 오류", message); app.Dispatcher.BeginInvoke(() => MessageBox.Show(message, "저장 오류")); };
        StartupTrace.Mark("준비"); var buddy = new Buddy();
        if (duoTest) RunOnce(TimeSpan.FromSeconds(1), buddy, "duo-live-failure.txt", () => CompanionIntegrationTest.Run(buddy));
        if (smokeTest) RunOnce(TimeSpan.FromSeconds(2), buddy, "smoke-test-failure.txt", () => { buddy.CaptureSmokeTest(); return System.Threading.Tasks.Task.CompletedTask; });
        app.Run(buddy);
        OrderedStore.FlushAsync().GetAwaiter().GetResult();
        if (OrderedStore.Errors.Length > 0) MessageBox.Show(string.Join("\n", OrderedStore.Errors), "일부 파일을 저장하지 못했습니다");
    }
    // Runs a self-check once the app is up, records a failure next to the program, then closes the app.
    static void RunOnce(TimeSpan delay, Buddy buddy, string failureFile, Func<System.Threading.Tasks.Task> check)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try { await check(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, failureFile), e.ToString()); Environment.ExitCode = 1; }
            finally { buddy.Close(); }
        };
        timer.Start();
    }
}
