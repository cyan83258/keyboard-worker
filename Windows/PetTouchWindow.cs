using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class PetTouchWindow : Window
{
    public PetTouchWindow(Buddy buddy)
    {
        var p = new StackPanel { Margin = new Thickness(22) }; p.Children.Add(Pastel.Text("마우스로 살살 쓰다듬기 ♡", 22));
        var petting = new CheckBox { Content = "마우스 쓰다듬기 감지", IsChecked = buddy.Pref.MousePetting };
        var hearts = new CheckBox { Content = "파스텔 하트가 마우스를 따라 떠오르기", IsChecked = buddy.Pref.PettingHearts };
        p.Children.Add(petting);
        p.Children.Add(hearts);
        p.Children.Add(Pastel.Text("캐릭터 위에서 클릭하지 않고 마우스를 몇 번 왕복해 주세요. 단순히 지나가거나 가만히 올려두면 쓰다듬기로 계산하지 않습니다. 드래그로 위치를 옮길 때는 반응하지 않아요. 쓰다듬기 대사와 이미지는 각 캐릭터 설정을 사용합니다.", 13));
        p.Children.Add(Pastel.Text("클릭 통과가 켜져 있으면 쓰다듬기를 감지하지 않습니다. 하트는 최대 12개로 제한하며 사라진 뒤에는 애니메이션 처리를 멈춥니다.", 12));
        p.Children.Add(Pastel.Button("저장하고 닫기", async () =>
        {
            var next = buddy.Pref.Snapshot();
            next.MousePetting = petting.IsChecked == true;
            next.PettingHearts = hearts.IsChecked == true;
            buddy.Apply(next, configureKeys: false, persist: false);
            await Storage.SaveAsync(buddy.Pref);
            Close();
        }));
        Pastel.Window(this, "쓰다듬기 · 하트 효과", p, 530, 420);
    }
}
