namespace Sharp.Shell.Commands;

// The owned command set. There is no PATH in this tier: a name is either in here or it is not ours.
public sealed class AppletRegistry
{
    private readonly Dictionary<string, IApplet> byName;

    public AppletRegistry(IReadOnlyList<IApplet> applets)
    {
        Applets = applets;
        byName = applets.ToDictionary(applet => applet.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<IApplet> Applets { get; }

    public IReadOnlyCollection<string> Names => byName.Keys;

    // CommandApplet answers questions about the registry it lives in, so the list is built first
    // and the registry handed back to it once it exists.
    public static AppletRegistry CreateDefault()
    {
        AppletRegistry? registry = null;

        List<IApplet> applets =
        [
            new LsApplet(),
            new CatApplet(),
            new HeadApplet(),
            new TailApplet(),
            new WcApplet(),
            new GrepApplet(),
            new FindApplet(),
            new SedApplet(),
            new AwkApplet("awk"),
            new AwkApplet("nawk"),
            new SortApplet(),
            new UniqApplet(),
            new CutApplet(),
            new TrApplet(),
            new BasenameApplet(),
            new DirnameApplet(),
            new RealpathApplet(),
            new PwdApplet(),
            new MkdirApplet(),
            new RmApplet(),
            new MvApplet(),
            new CpApplet(),
            new TouchApplet(),
            new EchoApplet(),
            new PrintfApplet(),
            new TestApplet("test"),
            new TestApplet("["),
            new CdApplet(),
            new ExportApplet(),
            new UnsetApplet(),
            new SetApplet(),
            new ShoptApplet(),
            new SetoptApplet("setopt", turningOn: true),
            new SetoptApplet("unsetopt", turningOn: false),
            new AliasApplet("alias", isDefining: true),
            new AliasApplet("unalias", isDefining: false),
            new SourceApplet("source"),
            new SourceApplet("."),
            new DirectoryStackApplet("pushd"),
            new DirectoryStackApplet("popd"),
            new DirectoryStackApplet("dirs"),
            new ReadApplet(),
            new TrueApplet(),
            new TrueApplet(":"),
            new FalseApplet(),
            new ExitApplet(),
            new CommandApplet(() => registry!.Names),
        ];

        registry = new AppletRegistry(applets);
        return registry;
    }

    public bool TryGet(string name, out IApplet applet) => byName.TryGetValue(name, out applet!);
}
