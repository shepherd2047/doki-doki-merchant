namespace DokiDokiMerchant;

/// <summary>
/// The once-per-climb date with the Merchant, unlocked at Darling. Three questions drawn from a small pool, each
/// answer scored by the game (not the model): +2 he melts, +1 he likes it, 0 meh, -1 he's hurt. The total picks
/// the reward. His reactions are written here so they're instant and always in character.
/// </summary>
internal static class Date
{
    public record Answer(string En, string Zh, int Score, string ReplyEn, string ReplyZh)
    {
        public string Text => Loc.T(En, Zh);
        public string Reply => Loc.T(ReplyEn, ReplyZh);
    }

    public record Question(string En, string Zh, Answer[] Answers)
    {
        public string Text => Loc.T(En, Zh);
    }

    public const int Rounds = 3;
    public const int Perfect = 5; // out of 6
    public const int Sweet = 2;

    private static readonly Question[] Pool =
    {
        new("He lets the tent flap fall shut and pours two cups of something that glows faintly. \"Tea. On the house. Don't get used to it.\"",
            "他放下帐篷的门帘，倒了两杯微微发光的东西。“茶。本店请客。别习惯了。”",
            new Answer[]
            {
                new("Toast: to your business!", "举杯：为你的生意干杯！", 2,
                    "[flustered] T-to my business. Yes. Good answer.", "[flustered] 为、为我的生意干杯。嗯。回答得不错。"),
                new("Sniff it suspiciously.", "怀疑地闻一闻。", 0,
                    "[sighs] It's not poisoned. Probably.", "[sighs] 没下毒。大概吧。"),
                new("How much does it really cost?", "这茶其实值多少钱？", 1,
                    "[smug] Ha! You're learning. Normally? Forty gold.", "[smug] 哈！你开窍了。平时嘛？四十金币。"),
            }),
        new("\"So.\" He fidgets with his sleeve. \"What is it you actually see in me?\"",
            "“所以。”他摆弄着袖子。“你到底看上我哪一点？”",
            new Answer[]
            {
                new("Your eyes. I assume you have eyes.", "你的眼睛。我猜你是有眼睛的。", 2,
                    "[flustered] I— of course I have eyes! ...You like them?", "[flustered] 我——我当然有眼睛！……你喜欢？"),
                new("Your prices. Ruthless. Attractive.", "你的定价。够狠。很迷人。", 1,
                    "[smug] Finally, someone who appreciates a good markup.", "[smug] 终于有人懂得欣赏好的加价了。"),
                new("Honestly? The relics.", "说实话？你的遗物。", -1,
                    "[cold] ...I see. The relics. Of course.", "[cold] ……懂了。遗物。当然了。"),
            }),
        new("\"Neow keeps asking about you,\" he mutters, tugging his hood lower. \"What should I tell her?\"",
            "“涅奥一直在打听你，”他嘟囔着，把兜帽拉得更低。“我该怎么跟她说？”",
            new Answer[]
            {
                new("Tell her I'm taken.", "告诉她我名花有主了。", 2,
                    "[whispering] ...Taken. Right. I'll tell her. Loudly.", "[whispering] ……名花有主。好。我会告诉她的。大声地。"),
                new("Tell her to mind her own business.", "让她少管闲事。", 1,
                    "[laughs] Ha! I'd pay to see her face.", "[laughs] 哈！我愿意花钱看她的表情。"),
                new("Who's Neow?", "涅奥是谁？", -1,
                    "[sighs] The giant whale who brings you back to life. Every time. Honestly.", "[sighs] 那条每次都把你复活的大鲸鱼。真是的。"),
            }),
        new("He slides a small wrapped box across the rug, not quite looking at you. \"Found it. In the Spire. Don't read into it.\"",
            "他把一个包好的小盒子推过地毯，眼睛不太敢看你。“捡的。在尖塔里。别想太多。”",
            new Answer[]
            {
                new("Open it slowly and smile.", "慢慢拆开，然后微笑。", 2,
                    "[flustered] Stop smiling. ...No, keep smiling.", "[flustered] 别笑了。……不，继续笑。"),
                new("Ask for the receipt.", "问他要发票。", 1,
                    "[smug] A customer after my own heart.", "[smug] 真是个懂我心思的顾客。"),
                new("Try to sell it back to him.", "试图把它卖回给他。", -1,
                    "[shrieking] You're haggling with me ON A DATE?", "[shrieking] 你在约会的时候跟我砍价？！"),
            }),
        new("\"When you reach the top,\" he says quietly, \"will you still come back down to visit?\"",
            "“等你爬到塔顶，”他轻声说，“还会下来看我吗？”",
            new Answer[]
            {
                new("I'll come back just for you.", "我会专程回来看你。", 2,
                    "[whispering] ...I'll keep the tent open. Just in case.", "[whispering] ……那我会一直开着帐篷。以防万一。"),
                new("I'll bring you the Spire's best loot.", "我会把塔里最好的宝贝带给你。", 1,
                    "[smug] Bring it to sell, and I'll give you a fair price. Almost fair.", "[smug] 带来卖给我，我给你个公道价。差不多公道。"),
                new("If I survive, I'm retiring far away.", "要是活下来，我就退休去远方。", -1,
                    "[cold] Fine. Good. I didn't want company anyway.", "[cold] 行。挺好。我本来也不想要人陪。"),
            }),
    };

    /// <summary>Three questions for one date, each with its answers shuffled.</summary>
    public static List<Question> Draw()
    {
        var rng = new Random();
        return Pool.OrderBy(_ => rng.Next()).Take(Rounds)
            .Select(q => q with { Answers = q.Answers.OrderBy(_ => rng.Next()).ToArray() })
            .ToList();
    }
}
