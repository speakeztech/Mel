namespace Mel.UI.ViewModels

open System
open System.ComponentModel
open System.Runtime.CompilerServices

type BaseViewModel() =
    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()
    
    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish
    
    member this.OnPropertyChanged([<CallerMemberName>] ?propertyName: string) =
        propertyChanged.Trigger(this, PropertyChangedEventArgs(defaultArg propertyName ""))
    
    member this.SetProperty<'T>(storage: 'T byref, value: 'T, [<CallerMemberName>] ?propertyName: string) =
        if not (obj.Equals(storage, value)) then
            storage <- value
            this.OnPropertyChanged(?propertyName = propertyName)
            true
        else
            false