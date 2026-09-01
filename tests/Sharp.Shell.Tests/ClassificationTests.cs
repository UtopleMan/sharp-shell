using Sharp.Shell;
using Sharp.Shell.Commands;
using Xunit;

namespace Sharp.Shell.Tests;

public class ClassificationTests
{
    private static Classification Classify(string commandLine) =>
        new CommandClassifier(AppletRegistry.CreateDefault()).Classify(commandLine);

    [Theory]
    [InlineData("ls -a")]
    [InlineData("cat f.txt | grep needle | head -3")]
    [InlineData("if true; then echo y; fi")]
    [InlineData("for i in a b; do echo $i; done")]
    [InlineData("x=1; echo ${x:-d}")]
    [InlineData("echo $(basename /a/b)")]
    public void OwnedLinesStayInTheSandbox(string commandLine)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal(ExecutionTier.Owned, classification.Tier);
        Assert.Empty(classification.UnownedPrograms);
    }

    [Theory]
    [InlineData("ls -a", false)]
    [InlineData("rm -rf x", true)]
    [InlineData("echo hi > f", true)]
    [InlineData("echo hi >> f", true)]
    [InlineData("cat f", false)]
    [InlineData("for i in a; do touch $i; done", true)]
    public void MutationIsReportedSeparatelyFromTheTier(string commandLine, bool mutates)
    {
        Assert.Equal(mutates, Classify(commandLine).Mutates);
    }

    [Fact]
    public void AnUnownedProgramEscalatesTheWholeLine()
    {
        Classification classification = Classify("git status");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Equal(["git"], classification.UnownedPrograms);
    }

    [Fact]
    public void AMixedPipelineEscalatesEntirelyAndNamesOnlyTheUnownedProgram()
    {
        Classification classification = Classify("ls | jq '.'");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Equal(["jq"], classification.UnownedPrograms);
        Assert.DoesNotContain("ls", classification.UnownedPrograms);
    }

    [Fact]
    public void EveryUnownedProgramInThePipelineIsNamed()
    {
        Classification classification = Classify("git status | grep x | curl -T -");

        Assert.Equal(["curl", "git"], classification.UnownedPrograms.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnsupportedFlagEscalatesAndNamesItsApplet()
    {
        Classification classification = Classify("ls -l");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Equal(["ls"], classification.UnownedPrograms);
        Assert.Contains("-l", classification.Reason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/bin/ls")]
    [InlineData("./script.sh")]
    [InlineData("sub/tool")]
    public void AnExplicitPathIsNeverAnOwnedName(string commandLine)
    {
        Assert.Equal(ExecutionTier.Native, Classify(commandLine).Tier);
    }

    [Fact]
    public void ACommandNameThatNeedsExpansionEscalates()
    {
        Classification classification = Classify("$CMD foo");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Contains("expansion", classification.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sleep 1 &", "background")]
    [InlineData("diff <(a) <(b)", "process substitution")]
    [InlineData("echo $$", "$$")]
    [InlineData("echo 'unterminated", "unterminated")]
    public void UnsupportedConstructsEscalateWithTheirReason(string commandLine, string mentioned)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Contains(mentioned, classification.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsupportedExpansionFormEscalates()
    {
        Classification classification = Classify("echo ${x@Q}");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Contains("@Q", classification.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnownedProgramInsideACommandSubstitutionEscalates()
    {
        Classification classification = Classify("echo $(git rev-parse HEAD)");

        Assert.Equal(ExecutionTier.Native, classification.Tier);
        Assert.Equal(["git"], classification.UnownedPrograms);
    }

    // POSIX short flags bundle. Measured against this project's own stored sessions, failing to
    // split them was 36% of all escalations — the sandbox refusing commands it can actually run.
    [Theory]
    [InlineData("grep -rn needle .")]
    [InlineData("grep -rln needle .")]
    [InlineData("ls -aR")]
    [InlineData("rm -rf dir")]
    [InlineData("cp -rf a b")]
    public void BundledShortFlagsAreSplitBeforeTheyAreJudged(string commandLine)
    {
        Assert.Equal(ExecutionTier.Owned, Classify(commandLine).Tier);
    }

    [Theory]
    [InlineData("ls -la")]
    [InlineData("ls -lt")]
    [InlineData("grep -A6 needle f")]
    [InlineData("cat -v f")]
    [InlineData("grep -qh needle f")]
    public void BundlingNeverTurnsAnUnimplementedFlagIntoASupportedOne(string commandLine)
    {
        Assert.Equal(ExecutionTier.Native, Classify(commandLine).Tier);
    }

    [Fact]
    public void BundlingSupportedFlagsStaysOwned()
    {
        Assert.Equal(ExecutionTier.Owned, Classify("grep -oh needle f").Tier);
    }

    // find spells its options as single-dash words. Splitting -name into -n -a -m -e would turn a
    // working command into an escalation, which is why expansion is per applet and not a rule.
    [Theory]
    [InlineData("find . -name '*.txt'")]
    [InlineData("find . -type f -maxdepth 2")]
    [InlineData("find . -iname x -print")]
    public void SingleDashWordOptionsAreNotTreatedAsBundles(string commandLine)
    {
        Assert.Equal(ExecutionTier.Owned, Classify(commandLine).Tier);
    }

    [Fact]
    public void AnUnownedProgramInARedirectionTargetIsStillJustAWord()
    {
        Assert.Equal(ExecutionTier.Owned, Classify("echo hi > git").Tier);
    }

    // sed is the first applet that both reads and writes depending on its arguments, and the first
    // whose operands are a program rather than data.
    [Theory]
    [InlineData("sed -n '1,50p' file.txt")]
    [InlineData("sed 's/a/b/' file.txt")]
    [InlineData("sed -E 's/(a)+/x/' file.txt")]
    [InlineData("printf 'a\\n' | sed -n '$p'")]
    [InlineData("sed -n '1,5p' \"$file\"")]
    [InlineData("sed 's/a/b/' \"$dir/notes.txt\"")]
    [InlineData("sed -e 's/a/b/' \"$file\"")]
    public void AReadOnlySedRunsConfinedWithoutClaimingToMutate(string commandLine)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal(ExecutionTier.Owned, classification.Tier);
        Assert.False(classification.Mutates);
    }

    [Theory]
    [InlineData("sed -i 's/a/b/' file.txt")]
    [InlineData("sed -n '/a/w out.txt' file.txt")]
    [InlineData("sed 's/a/b/w out.txt' file.txt")]
    public void AWritingSedIsOwnedAndDeclaresItsMutation(string commandLine)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal(ExecutionTier.Owned, classification.Tier);
        Assert.True(classification.Mutates);
    }

    [Theory]
    [InlineData("sed \"s/$pattern/y/\" file.txt")]
    [InlineData("sed \"$script\" file.txt")]
    [InlineData("sed -e \"s/${name}/x/\" file.txt")]
    public void ASedScriptThatNeedsExpandingEscalates(string commandLine)
    {
        Assert.Equal(ExecutionTier.Native, Classify(commandLine).Tier);
    }

    // The hook is precise: only the words that are program text have to be literal, so a file operand
    // that still needs expanding does not push the line out to the native tier.
    [Fact]
    public void OnlyTheScriptHasToBeLiteralBeforeExpansion()
    {
        Assert.Equal(ExecutionTier.Owned, Classify("sed -n '1,5p' \"$file\"").Tier);
        Assert.Equal(ExecutionTier.Native, Classify("sed -n \"$script\" file.txt").Tier);
    }

    [Theory]
    [InlineData("awk '{print $1}' file.txt", false)]
    [InlineData("awk -F: '{print $1}' \"$file\"", false)]
    [InlineData("awk 'END {print NR}' file.txt", false)]
    [InlineData("awk '{print > \"out\"}' f", true)]
    [InlineData("awk -f p.awk f", true)]
    public void AnOwnedAwkDeclaresWhetherItWrites(string commandLine, bool mutates)
    {
        Classification classification = Classify(commandLine);

        Assert.Equal(ExecutionTier.Owned, classification.Tier);
        Assert.Equal(mutates, classification.Mutates);
    }

    [Theory]
    [InlineData("awk \"$prog\" f")]
    [InlineData("awk '{system(\"ls\")}'")]
    [InlineData("awk '{while ((getline l) > 0) n++}'")]
    [InlineData("awk '{print | \"cat\"}'")]
    [InlineData("awk --posix '{print}'")]
    [InlineData("gawk '{print}'")]
    public void AwkConstructsTheSandboxCannotHonourEscalate(string commandLine)
    {
        Assert.Equal(ExecutionTier.Native, Classify(commandLine).Tier);
    }

    [Theory]
    [InlineData("sed 'e ls' file.txt")]
    [InlineData("sed 's/a/b/e' file.txt")]
    [InlineData("sed 's/a\\|ab/x/' file.txt")]
    [InlineData("sed --debug 's/a/b/' file.txt")]
    [InlineData("sed --follow-symlinks -i 's/a/b/' file.txt")]
    [InlineData("sed --version")]
    public void SedConstructsTheSandboxCannotHonourEscalate(string commandLine)
    {
        Assert.Equal(ExecutionTier.Native, Classify(commandLine).Tier);
    }
}
