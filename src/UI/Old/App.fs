namespace Mel.UI

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml
open Avalonia.Themes.Fluent
open Mel.UI.CompositionRoot

type App() =
    inherit Application()
    
    let mutable compositionRoot: CompositionRoot option = None
    
    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Avalonia.Styling.ThemeVariant.Dark
        this.Resources.Add("FileSizeConverter", Mel.UI.Views.Converters.FileSizeConverter())
        this.Resources.Add("TimeSpanConverter", Mel.UI.Views.Converters.TimeSpanConverter())
        this.Resources.Add("BoolToVisibilityConverter", Mel.UI.Views.Converters.BoolToVisibilityConverter())
    
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let root = CompositionRoot()
            compositionRoot <- Some root
            
            let mainVm = root.GetMainViewModel()
            let trayService = root.GetTrayService()
            
            trayService.Initialize()
            
            desktop.ShutdownMode <- ShutdownMode.OnExplicitShutdown
            
        | _ -> ()
        
        base.OnFrameworkInitializationCompleted()
    
    member _.CompositionRoot = compositionRoot