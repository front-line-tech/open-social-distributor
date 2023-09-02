using System.Text;
using DistributorLib.Network;

namespace DistributorLib.Post.Formatters;

public class LengthLimitedPostFormatter : AbstractPostFormatter
{
    public static string BREAK_CODE = "$$";
    public enum BreakBehaviour { None, NewParagraph, NewPost }

    protected bool linkInText;
    protected int messageLengthLimit;
    protected int subsequentLimits;
    protected BreakBehaviour breakBehaviour;

    public LengthLimitedPostFormatter(NetworkType network, int limit, int subsequentLimits, bool linkInText, BreakBehaviour breakBehaviour = BreakBehaviour.NewPost) : base(network)
    {
        this.messageLengthLimit = limit;
        this.subsequentLimits = subsequentLimits;
        this.linkInText = linkInText;
        this.breakBehaviour = breakBehaviour;
    }

    public override IEnumerable<string> FormatText(ISocialMessage message)
    {
        return new WordWrapFormatter()
            .WithNetwork(Network)
            .WithLimit(messageLengthLimit)
            .WithSubsequentLimits(subsequentLimits)
            .WithMessage(message)
            .WithLinkInText(linkInText)
            .WithBreakBehaviour(breakBehaviour, BREAK_CODE)
            .Build();
    }

    public class WordWrapFormatter
    {
        public List<string>? Messages { get; private set; }

        private int currentMessageIndex;
        private StringBuilder? currentMessage;
        private ISocialMessage? message;
        private NetworkType network;
        private int limit;
        private int subsequentLimits;
        private bool link;
        private string msgBreak;
        private BreakBehaviour msgBreakBehaviour;

        public WordWrapFormatter WithNetwork(NetworkType network) { this.network = network; return this; }
        public WordWrapFormatter WithLimit(int limit) { this.limit = limit; return this; }
        public WordWrapFormatter WithSubsequentLimits(int subsequentLimits) { this.subsequentLimits = subsequentLimits; return this; }
        public WordWrapFormatter WithMessage(ISocialMessage message) { this.message = message; return this; }
        public WordWrapFormatter WithLinkInText(bool link) { this.link = link; return this; }
        public WordWrapFormatter WithBreakBehaviour(BreakBehaviour behaviour, string msgBreak) { this.msgBreak = msgBreak; this.msgBreakBehaviour = behaviour; return this; }

        public IEnumerable<string> Build()
        {
            var included = message!.Parts.Where(part => link || part.Part != SocialMessagePart.Link);
            var strings = included.Select(part => part.ToStringFor(network)).Where(x => x != null).ToList();
            var words = string.Join(' ', strings).Split(' ');

            Messages = new List<string>();
            currentMessageIndex = 1;
            currentMessage = new StringBuilder();

            for (int i = 0; i < words.Length; i++)
            {
                var currentLimit = Messages.Count() == 0 ? limit : subsequentLimits;
                PerformAction(words, i, currentLimit);
            }
            CompleteMessage();
            return Messages;
        }

        private void PerformAction(IEnumerable<string> words, int index, int currentLimit)
        {
            var word = words.ElementAt(index);
            var nextWord = index + 1 < words.Count() ? words.ElementAt(index+1) : null;

            var decision = DetermineAction(currentMessage!, currentLimit, word, nextWord, IndexWord(currentMessageIndex));
            Console.WriteLine($"Decision: {decision} for: {word}");
            switch (decision)
            {
                case Decision.AddWord:
                    currentMessage!.Append($"{(currentMessage!.Length == 0 ? "" : " ")}{word}");
                    break;
                case Decision.AddIndex:
                    currentMessage!.Append($"{(currentMessage!.Length == 0 ? "" : " ")}{IndexWord(currentMessageIndex)}");
                    CompleteMessage();
                    PerformAction(words, index, currentLimit);
                    break;
                case Decision.AddNothing:
                    CompleteMessage();
                    PerformAction(words, index, currentLimit);
                    break;
                case Decision.NewParagraph:
                    currentMessage!.Append("\n\n");
                    break;
                case Decision.NewPost:
                    currentMessage!.Append($"{(currentMessage!.Length == 0 ? "" : " ")}{IndexWord(currentMessageIndex)}");
                    CompleteMessage();
                    break;
                case Decision.VeryLongWord:
                    CompleteMessage();
                    var chunks = word.Chunk(currentLimit).Select(s => new string(s)).ToList();
                    foreach (var chunk in chunks)
                    {
                        currentMessage!.Append(chunk);
                        CompleteMessage();
                    }
                    break;
                case Decision.Ignore:
                    break;
                case Decision.Finish:
                    CompleteMessage();
                    break;
            }
        }

        private string IndexWord(int index) => $"/{index}";

        private void CompleteMessage()
        {
            if (currentMessage!.Length > 0) 
            {
                Messages!.Add(currentMessage!.ToString());
                currentMessageIndex++;
            }
            currentMessage = new StringBuilder();
        }

        private enum Decision { Ignore, NewParagraph, NewPost, AddWord, AddIndex, VeryLongWord, AddNothing, Finish }

        private Decision DetermineAction(StringBuilder message, int currentLimit, string? thisWord, string? nextWord, string indexWord)
        {
            if (thisWord == null) return Decision.Finish;

            if (thisWord == msgBreak)
            {
                switch (msgBreakBehaviour)
                {
                    case BreakBehaviour.None:
                        return Decision.Ignore;
                    case BreakBehaviour.NewParagraph:
                        return Decision.NewParagraph;
                    case BreakBehaviour.NewPost:
                        return Decision.NewPost;
                    default:
                        throw new NotImplementedException($"Break behaviour not implemented: {msgBreakBehaviour}");
                }
            }

            bool firstWordInMessage = message.Length == 0;
            int prefixChars = firstWordInMessage ? 0 : 1;
            bool canFitThisWord = message.Length + thisWord!.Length + prefixChars <= currentLimit;
            bool canFitIndexWord = message.Length + indexWord.Length + prefixChars <= currentLimit;
            bool canFitThisAndNextWord = nextWord == null ? false : message.Length + thisWord!.Length + nextWord!.Length + prefixChars + 1 <= currentLimit;
            bool canFitThisAndIndexWord = message.Length + thisWord!.Length + indexWord.Length + prefixChars + 1 <= currentLimit;
            bool isVeryLongWord = thisWord.Length > currentLimit;

            if (canFitThisAndNextWord || canFitThisAndIndexWord) return Decision.AddWord;
            if (message.Length == 0 && canFitThisWord) return Decision.AddWord;
            if (message.Length == 0 && isVeryLongWord) return Decision.VeryLongWord;

            return canFitIndexWord
                ? Decision.AddIndex
                : Decision.AddNothing; // shouldn't happen, might happen I guess
        }

    }
}