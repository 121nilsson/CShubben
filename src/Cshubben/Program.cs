using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<RootComponent>("Cshubben");

/// <summary>Root component. The app grows from here (navigation, pages, state).</summary>
sealed class RootComponent : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            Heading("Cshubben"),
            Heading($"Clicks: {count}"),
            HStack(8,
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))
            )
        );
    }
}
