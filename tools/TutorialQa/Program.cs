using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var outputDirectory = Path.GetFullPath(args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "tutorial-integration"));
        Directory.CreateDirectory(outputDirectory);
        var settingsPath = Path.Combine(outputDirectory, "tutorial-settings.json");
        Environment.SetEnvironmentVariable("DUST_SETTINGS_FILE", settingsPath);
        if (File.Exists(settingsPath)) File.Delete(settingsPath);

        var gameAssembly = Assembly.Load("Dust");
        var gameType = gameAssembly.GetType("Dust.GameForm", throwOnError: true)!;
        var modeField = gameType.GetField("_mode", InstanceFlags)!;
        var titleMode = Enum.Parse(modeField.FieldType, "Title");
        var offerMode = Enum.Parse(modeField.FieldType, "TutorialOffer");
        var tutorialMode = Enum.Parse(modeField.FieldType, "Tutorial");

        using (var form = CreateForm(gameType))
        {
            Require(Equals(modeField.GetValue(form), titleMode),
                "Constructing the form hijacked headless QA before the Shown event.");
            Invoke(gameType, form, "OfferTutorialOnFirstShown");
            Require(Equals(modeField.GetValue(form), offerMode),
                "A fresh profile was not offered the current tutorial.");
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-offer.png"));

            Invoke(gameType, form, "BeginTutorial");
            Require(Equals(modeField.GetValue(form), tutorialMode),
                "Accepting the offer did not open the tutorial.");
            VerifyVersion(settingsPath, offered: 1, completed: 0);
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-movement.png"));

            Press(gameType, form, Keys.Right);
            Press(gameType, form, Keys.D);
            Press(gameType, form, Keys.Right);
            Press(gameType, form, Keys.Enter);
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-interaction.png"));
            Press(gameType, form, Keys.E);
            Press(gameType, form, Keys.Enter);
            Press(gameType, form, Keys.Q);
            Require(Field<bool>(gameType, form, "_tutorialFileOpen"),
                "The mission-file lesson did not respond to Q.");
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-file.png"));
            Press(gameType, form, Keys.Q);
            Press(gameType, form, Keys.Enter);
            Press(gameType, form, Keys.Space);
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-perk.png"));
            Press(gameType, form, Keys.Enter);
            Press(gameType, form, Keys.Down);
            Press(gameType, form, Keys.S);
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-hollow.png"));
            Press(gameType, form, Keys.Enter);
            Require((int)Property(gameType, form, "TutorialStageForQa") == 5,
                "The complete tutorial did not reach its extraction track.");
            SaveFrame(gameType, form, Path.Combine(outputDirectory, "tutorial-extraction.png"));
            Press(gameType, form, Keys.Enter);
            VerifyVersion(settingsPath, offered: 1, completed: 1);
            Require(Equals(modeField.GetValue(form), titleMode),
                "Completing the tutorial did not return to routing.");
        }

        using (var returningForm = CreateForm(gameType))
        {
            Invoke(gameType, returningForm, "OfferTutorialOnFirstShown");
            Require(Equals(modeField.GetValue(returningForm), titleMode),
                "A completed tutorial was offered again on the same version.");
        }

        // This models every profile written before the tutorial fields existed.
        File.WriteAllText(settingsPath, "{\"Volume\":55,\"TotalCredits\":120}");
        using (var migratedForm = CreateForm(gameType))
        {
            Invoke(gameType, migratedForm, "OfferTutorialOnFirstShown");
            Require(Equals(modeField.GetValue(migratedForm), offerMode),
                "A pre-tutorial settings file did not receive the one-time offer.");
            Invoke(gameType, migratedForm, "DeclineTutorialOffer");
            VerifyVersion(settingsPath, offered: 1, completed: 0);
        }
        using (var declinedForm = CreateForm(gameType))
        {
            Invoke(gameType, declinedForm, "OfferTutorialOnFirstShown");
            Require(Equals(modeField.GetValue(declinedForm), titleMode),
                "A declined tutorial offer appeared more than once.");
        }

        Console.WriteLine(
            $"Tutorial QA passed: fresh offer, old-profile migration, persistence, six tracks, and rendering. Output: {outputDirectory}");
    }

    private static Form CreateForm(Type gameType)
    {
        var form = (Form)Activator.CreateInstance(gameType)!;
        form.ClientSize = new Size(1280, 800);
        gameType.GetField("_timer", InstanceFlags)!.GetValue(form)!
            .As<System.Windows.Forms.Timer>().Stop();
        return form;
    }

    private static void SaveFrame(Type gameType, Form form, string path)
    {
        using var bitmap = new Bitmap(1280, 800);
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size));
        gameType.GetMethod("PaintScene", InstanceFlags)!.Invoke(form, [form, args]);
        bitmap.Save(path);
    }

    private static void VerifyVersion(string settingsPath, int offered, int completed)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var root = document.RootElement;
        Require(root.GetProperty("TutorialOfferVersion").GetInt32() == offered,
            "The tutorial offer version was not persisted.");
        Require(root.GetProperty("TutorialCompletedVersion").GetInt32() == completed,
            "The tutorial completion version was not persisted correctly.");
    }

    private static object Property(Type type, object instance, string name) =>
        type.GetProperty(name, InstanceFlags)!.GetValue(instance)!;

    private static T Field<T>(Type type, object instance, string name) =>
        (T)type.GetField(name, InstanceFlags)!.GetValue(instance)!;

    private static void Invoke(Type type, object instance, string name) =>
        type.GetMethod(name, InstanceFlags)!.Invoke(instance, null);

    private static void Press(Type type, object instance, Keys key) =>
        type.GetMethod("HandleTutorialKey", InstanceFlags)!
            .Invoke(instance, [new KeyEventArgs(key)]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T As<T>(this object value) => (T)value;
}
