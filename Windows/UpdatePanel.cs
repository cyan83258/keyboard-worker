using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

// The update card on the settings' system page. Updating is by hand only: one button looks for a newer version,
// and a second one (shown when there is one) installs it. Nothing is checked or installed without being asked.
public sealed class UpdatePanel : StackPanel
{
    readonly Buddy buddy; readonly Window owner; readonly TextBlock found = Pastel.Text("", 13), notes = Pastel.Text("", 12), status = Pastel.Text("", 12); readonly Button check, install;
    ReleaseInfo? release; CancellationTokenSource? work;
    public UpdatePanel(Buddy buddy, Window owner)
    {
        this.buddy = buddy; this.owner = owner;
        Children.Add(Pastel.Text("업데이트", 15));
        Children.Add(Pastel.Text("지금 버전 " + AppInfo.Version + (Updater.Configured ? "  ·  github.com/" + Updater.Repository : ""), 13));
        var buttons = new WrapPanel();
        check = Pastel.Button("업데이트 확인", async () => await Check(), true); install = Pastel.Button("업데이트하고 다시 시작", async () => await Install()); install.Visibility = Visibility.Collapsed;
        buttons.Children.Add(check); buttons.Children.Add(install); Children.Add(buttons);
        found.Visibility = notes.Visibility = Visibility.Collapsed; notes.MaxHeight = 150; Children.Add(found); Children.Add(notes); Children.Add(status);
        if (!Updater.Configured) { check.IsEnabled = false; status.Text = "이 프로그램에는 업데이트를 받아 올 주소가 정해져 있지 않아요. 새 버전은 받은 곳에서 다시 내려받아 주세요."; }
        else status.Text = "버튼을 누를 때만 확인합니다. 자동으로 확인하거나 설치하지 않아요. 설정과 캐릭터는 프로그램 폴더 밖에 있어서 그대로 남습니다.";
        owner.Closed += (_, _) => work?.Cancel();
    }
    async Task Check()
    {
        check.IsEnabled = false; install.Visibility = found.Visibility = notes.Visibility = Visibility.Collapsed; status.Text = "최신 버전을 확인하는 중…"; release = null;
        work = new CancellationTokenSource();
        try
        {
            var latest = await Updater.Check(work.Token);
            if (!Updater.Newer(latest, AppInfo.Version)) { status.Text = "이미 최신 버전이에요 ♡ (최신: " + latest.Tag + ")"; return; }
            release = latest; found.Text = "새 버전이 있어요: " + latest.Title + " (" + latest.Tag + ")" + (latest.Size > 0 ? "  ·  " + (latest.Size / 1048576.0).ToString("0.0") + "MB" : ""); found.Visibility = Visibility.Visible;
            if (latest.Notes.Length > 0) { notes.Text = latest.Notes; notes.Visibility = Visibility.Visible; }
            install.Visibility = Visibility.Visible; status.Text = "업데이트하면 새 파일을 내려받아 프로그램 파일을 바꾸고 바로 다시 시작합니다.";
        }
        catch (OperationCanceledException) { status.Text = "확인을 멈췄어요."; }
        catch (Exception e) { status.Text = "확인하지 못했습니다. " + e.Message; }
        finally { check.IsEnabled = true; work?.Dispose(); work = null; }
    }
    async Task Install()
    {
        var chosen = release; if (chosen == null) return;
        if (buddy.IsChatBusy) { status.Text = "답변을 만드는 중이에요. 끝난 뒤에 업데이트해 주세요."; return; }
        if (MessageBox.Show(owner, chosen.Title + " (" + chosen.Tag + ")(으)로 업데이트할까요?\n\n새 파일을 내려받아 프로그램 파일을 바꾸고 바로 다시 시작합니다. 설정과 캐릭터는 그대로 남아요.", "업데이트", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        check.IsEnabled = install.IsEnabled = false; work = new CancellationTokenSource();
        try { await buddy.ApplyUpdate(chosen, text => status.Text = text, work.Token); }
        catch (OperationCanceledException) { status.Text = "업데이트를 멈췄어요. 프로그램은 그대로입니다."; }
        catch (Exception e) { status.Text = "업데이트하지 못했습니다. 프로그램은 그대로입니다. " + e.Message; }
        finally { check.IsEnabled = install.IsEnabled = true; work?.Dispose(); work = null; }
    }
}
