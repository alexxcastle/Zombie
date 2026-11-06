using UnityEngine;
using UnityEngine.UIElements;

// Game-over screen, built with the shared menu design system (ElderboomUi).
// Operator and difficulty are chosen only in the main menu.
public sealed partial class ElderboomGame
{
    PanelSettings menuPanel;
    VisualElement menuUi, gameOver, statsRow;
    Label overSubtitle;
    State shownState = (State)(-1);

    void LateUpdate()
    {
        if (menuUi == null) BuildMenuUi();
        if (state == shownState) return;
        shownState = state;
        ElderboomUi.Show(gameOver, state == State.GameOver);
        if (state == State.GameOver) { FillGameOver(); ElderboomUi.ReplayIn(gameOver, 70); }
    }

    static VisualElement Column(VisualElement parent, float top)
    {
        var c = new VisualElement { pickingMode = PickingMode.Ignore };
        c.AddToClassList("eb-fill");
        c.style.alignItems = Align.Center;
        c.style.paddingTop = top;
        parent.Add(c);
        return c;
    }

    static T In<T>(T e) where T : VisualElement { e.AddToClassList("eb-enter"); e.AddToClassList("eb-enter--up"); return e; }

    static VisualElement CenteredBar()
    {
        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList("eb-accent-bar");
        bar.style.alignSelf = Align.Center;
        return bar;
    }

    void BuildMenuUi()
    {
        menuUi = ElderboomUi.CreateRoot(gameObject, "GameMenuUi", 15, out menuPanel, out _);
        menuUi.pickingMode = PickingMode.Ignore;

        // ---- Game over ----
        gameOver = new VisualElement();
        gameOver.AddToClassList("eb-fill");
        menuUi.Add(gameOver);
        ElderboomUi.Fill(gameOver, new Color(.03f, 0f, 0f, .72f));
        ElderboomUi.Gradient(gameOver, new Color(0, 0, 0, .2f), new Color(0, 0, 0, .9f), false, 1.4f);
        var over = Column(gameOver, 230);
        over.Add(In(Centered(ElderboomUi.Text(null, "RUN ENDED", "eb-h3"))));
        var title = In(Centered(ElderboomUi.Text(null, "ELDERBOOM FALLEN", "eb-h1", "eb-accent")));
        title.style.fontSize = 84; title.style.letterSpacing = 10;
        over.Add(title);
        overSubtitle = In(Centered(ElderboomUi.Text(null, "", "eb-caption")));
        over.Add(overSubtitle);
        over.Add(In(CenteredBar()));
        statsRow = In(new VisualElement { pickingMode = PickingMode.Ignore });
        statsRow.AddToClassList("eb-row");
        statsRow.style.marginTop = 40; statsRow.style.marginBottom = 56;
        over.Add(statsRow);
        var buttons = In(new VisualElement { pickingMode = PickingMode.Ignore });
        buttons.AddToClassList("eb-row");
        over.Add(buttons);
        ElderboomUi.Button(buttons, "PLAY AGAIN", () => BeginSession(selectedOperator, selectedDifficulty), true, 240).style.marginRight = 16;
        ElderboomUi.Button(buttons, "MAIN MENU", () => UnityEngine.SceneManagement.SceneManager.LoadScene("ZombieTest_Menu"), false, 240);

        ElderboomUi.Show(gameOver, false);
    }

    static Label Centered(Label l) { l.style.unityTextAlign = TextAnchor.MiddleCenter; return l; }

    void FillGameOver()
    {
        var op = Operator;
        overSubtitle.text = (op != null ? op.displayName : selectedOperator) + "   ·   " + selectedDifficulty;
        int minutes = Mathf.FloorToInt(survivedSeconds / 60f), seconds = Mathf.FloorToInt(survivedSeconds % 60f);
        float accuracy = shotsFired > 0 ? 100f * shotsHit / shotsFired : 0f;
        statsRow.Clear();
        foreach (var (value, label) in new[]
        {
            (wave.ToString(), "WAVE REACHED"),
            (kills.ToString(), "KILLS"),
            (totalScore.ToString("N0"), "SCORE"),
            (accuracy.ToString("0") + "%", "ACCURACY"),
            (minutes + ":" + seconds.ToString("00"), "SURVIVED")
        })
        {
            var stat = new VisualElement { pickingMode = PickingMode.Ignore };
            stat.AddToClassList("eb-stat");
            stat.style.paddingLeft = stat.style.paddingRight = 34;
            if (statsRow.childCount > 0) { stat.style.borderLeftWidth = 1; stat.style.borderLeftColor = new Color(1, 1, 1, .1f); }
            ElderboomUi.Text(stat, value, "eb-stat-value");
            ElderboomUi.Text(stat, label, "eb-stat-label");
            statsRow.Add(stat);
        }
        gameOver.schedule.Execute(() => gameOver.Query<Button>().First()?.Focus()).StartingIn(400);
    }
}
