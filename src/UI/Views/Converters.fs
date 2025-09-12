module Mel.UI.Views.Converters

open System
open System.Globalization
open Avalonia.Data.Converters

type BoolToVisibilityConverter() =
    interface IValueConverter with
        member _.Convert(value, targetType, parameter, culture) =
            match value with
            | :? bool as b -> 
                if b then box Avalonia.Controls.Primitives.ScrollBarVisibility.Visible
                else box Avalonia.Controls.Primitives.ScrollBarVisibility.Collapsed
            | _ -> box Avalonia.Controls.Primitives.ScrollBarVisibility.Collapsed
        
        member _.ConvertBack(value, targetType, parameter, culture) =
            raise (NotImplementedException())

type FileSizeConverter() =
    interface IValueConverter with
        member _.Convert(value, targetType, parameter, culture) =
            match value with
            | :? int64 as size ->
                let kb = float size / 1024.0
                let mb = kb / 1024.0
                let gb = mb / 1024.0
                
                if gb >= 1.0 then
                    box (sprintf "%.2f GB" gb)
                elif mb >= 1.0 then
                    box (sprintf "%.2f MB" mb)
                elif kb >= 1.0 then
                    box (sprintf "%.2f KB" kb)
                else
                    box (sprintf "%d bytes" size)
            | _ -> box "0 bytes"
        
        member _.ConvertBack(value, targetType, parameter, culture) =
            raise (NotImplementedException())

type TimeSpanConverter() =
    interface IValueConverter with
        member _.Convert(value, targetType, parameter, culture) =
            match value with
            | :? TimeSpan as ts ->
                if ts.TotalHours >= 1.0 then
                    box (sprintf "%02d:%02d:%02d" (int ts.TotalHours) ts.Minutes ts.Seconds)
                else
                    box (sprintf "%02d:%02d" ts.Minutes ts.Seconds)
            | _ -> box "00:00"
        
        member _.ConvertBack(value, targetType, parameter, culture) =
            raise (NotImplementedException())