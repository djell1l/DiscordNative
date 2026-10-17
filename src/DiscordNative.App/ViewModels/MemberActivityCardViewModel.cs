using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiscordNative.Protocol.Models;

namespace DiscordNative.App.ViewModels;

public sealed class MemberActivityCardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public DiscordUser User { get; }
    public DiscordActivity Activity { get; }
    public string? VoiceChannelName { get; }

    public MemberActivityCardViewModel(DiscordUser user, DiscordActivity activity, string? voiceChannelName = null)
    {
        User = user;
        Activity = activity;
        VoiceChannelName = voiceChannelName;
    }

    public string UserName => User.DisplayName;
    public string UserStatusColor => User.StatusColor;
    public string UserAvatarUrl => User.AvatarUrl;

    public string ActivityName => Activity.Name;
    public string? LargeImageUrl => Activity.LargeImageUrl ?? Activity.SmallImageUrl ?? User.CurrentGameIconUrl;
    public bool HasLargeImage => !string.IsNullOrWhiteSpace(LargeImageUrl);

    public string SubtitleLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Activity.ElapsedText))
            {
                parts.Add(Activity.ElapsedText);
            }
            else if (!string.IsNullOrWhiteSpace(Activity.Details))
            {
                parts.Add(Activity.Details);
            }

            if (!string.IsNullOrWhiteSpace(VoiceChannelName))
            {
                parts.Add($"🔊 {VoiceChannelName}");
            }
            else if (!string.IsNullOrWhiteSpace(Activity.State))
            {
                parts.Add(Activity.State);
            }

            return parts.Count > 0 ? string.Join("  •  ", parts) : "En cours";
        }
    }

    public string TooltipText => $"{User.DisplayName}\n{Activity.Name}\n{SubtitleLine}";
}
