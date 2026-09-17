using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Mud.HttpUtils.Client")]
[assembly: InternalsVisibleTo("Mud.HttpUtils.Client.Tests")]
[assembly: InternalsVisibleTo("Mud.HttpUtils")]
// M5-HC-05：Resilience 需读取 IRequestContentReplayHint 做重试不可行性预判
[assembly: InternalsVisibleTo("Mud.HttpUtils.Resilience")]
[assembly: InternalsVisibleTo("Mud.HttpUtils.Resilience.Tests")]
