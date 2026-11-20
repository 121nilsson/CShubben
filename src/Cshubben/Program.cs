using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<RootComponent>("Cshubben");

/// <summary>Startkomponent. Här byggs appen ut (navigation, sidor, state).</summary>
sealed class RootComponent : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            Heading("Cshubben"),
            Heading($"Klick: {count}"),
            HStack(8,
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))
            )
        );
    }
}
