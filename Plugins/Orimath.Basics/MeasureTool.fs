﻿namespace Orimath.Basics
open Orimath.Core
open Orimath.FoldingInstruction
open Orimath.Plugins
open ApplicativeProperty.PropOperators

type MeasureTool(workspace: IWorkspace) =
    let paper = workspace.Paper
    let instruction = FoldingInstruction()
    let (|FreePoint|_|) (dt: OperationTarget) =
        match dt.Target with
        | DisplayTarget.Point(point) -> Some(point)
        | DisplayTarget.Layer(_) -> Some(dt.Point)
        | _ -> None
    let (|LineOrEdge|_|) (dt: OperationTarget) =
        match dt.Target with
        | DisplayTarget.Line(line) -> Some(line.Line, dt.Point)
        | DisplayTarget.Edge(edge) -> Some(edge.Line.Line, dt.Point)
        | _ -> None

    member _.GetDistanceLine(source, target) =
        match source, target with
        | FreePoint(p1), FreePoint(p2) -> LineSegment.FromPoints(p1, p2)
        | FreePoint(p1), LineOrEdge(l1, _) 
        | LineOrEdge(l1, _), FreePoint(p1) -> LineSegment.FromPoints(p1, Line.perpFoot p1 l1)
        | LineOrEdge(l1, p1), LineOrEdge(l2, _) ->
            match Line.cross l1 l2 with
            | Some(crossPoint) ->
                let cosTheta = abs (l1.XFactor * l2.XFactor + l1.YFactor * l2.YFactor)
                let sinTheta = -sqrt(1.0 - cosTheta * cosTheta)
                Some(LineSegment.FromFactorsAndPoint(sinTheta, cosTheta, crossPoint))
            | None ->
                match Line.cross l2 (Line.FromFactorsAndPoint(l1.YFactor, -l1.XFactor, p1)) with
                | Some(p2) -> LineSegment.FromPoints(p1, p2)
                | None -> None
        | _ -> None
    
    member _.ClearSelection() =
        paper.SelectedLayers .<- array.Empty()
        paper.SelectedPoints .<- array.Empty()
        paper.SelectedLines .<- array.Empty()
        paper.SelectedEdges .<- array.Empty()

    interface ITool with
        member _.Name = "計測"
        member _.ShortcutKey = "Ctrl+M"
        member _.Icon = Internal.getIcon "measure"
        member this.OnActivated() = this.ClearSelection()
        member _.OnDeactivated() = ()

    interface IClickTool with
        member this.OnClick(_, _) = this.ClearSelection()

    interface IDragTool with
        member _.BeginDrag(source, _) =
            match source with
            | FreePoint _ | LineOrEdge _ -> true
            | _ -> false

        member this.DragEnter(source, target, _) =
            match this.GetDistanceLine(source, target) with
            | Some(l) -> instruction.Lines .<- [| { Line = l; Color = InstructionColor.Gray } |]
            | None -> ()
            
            match target with
            | FreePoint _ | LineOrEdge _ -> true
            | _ -> false

        member this.DragOver(source, target, _) =
            match this.GetDistanceLine(source, target) with
            | Some(l) -> instruction.Lines .<- [| { Line = l; Color = InstructionColor.Gray } |]
            | None -> ()

            match target with
            | FreePoint _ | LineOrEdge _ -> true
            | _ -> false

        member _.DragLeave(_, target, _) =
            instruction.Lines .<- array.Empty()
            match target with
            | FreePoint _ | LineOrEdge _ -> true
            | _ -> false

        member this.Drop(source, target, modifier) =
            paper.SelectedLayers .<- array.Empty()
            paper.SelectedPoints .<- array.Empty()
            paper.SelectedEdges .<- array.Empty()
            paper.SelectedLines .<- 
                if modifier.HasFlag(OperationModifier.Shift)
                then Array.append paper.SelectedLines.Value (Option.toArray(this.GetDistanceLine(source, target)))
                else Option.toArray(this.GetDistanceLine(source, target))
            instruction.Lines .<- array.Empty()

    interface IFoldingInstructionTool with
        member _.FoldingInstruction = instruction
