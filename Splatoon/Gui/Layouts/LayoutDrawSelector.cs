﻿using Dalamud.Interface.Colors;
using ECommons.LanguageHelpers;
using Newtonsoft.Json;
using Splatoon.Utility;

namespace Splatoon.ConfigGui.CGuiLayouts;

internal static class LayoutDrawSelector
{
    internal static Layout CurrentLayout = null;
    internal static Element CurrentElement = null;
    internal static void DrawSelector(this Layout layout, string group, int index)
    {
        if (CGui.layoutFilter != "" && !layout.GetName().Contains(CGui.layoutFilter, StringComparison.OrdinalIgnoreCase))
        {
            if(CGui.ScrollTo == layout)
            {
                CGui.ScrollTo = null;
            }
            return;
        }
        ImGui.PushID(layout.GUID);
        {
            var col = false;
            if (!layout.Enabled)
            {
                col = true;
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3);
            }
            ImGui.SetCursorPosX(group == null ? 0 : 10);
            var curpos = ImGui.GetCursorScreenPos();
            var contRegion = ImGui.GetContentRegionAvail().X;
            if (CGui.ScrollTo == layout)
            {
                ImGui.SetScrollHereY();
                CGui.ScrollTo = null;
            }
            if (ImGui.Selectable($"{layout.GetName()}", CurrentLayout == layout))
            {
                if (CurrentLayout == layout && CurrentElement == null)
                {
                    CurrentLayout = null;
                    if (P.Config.FocusMode)
                    {
                        CGui.ScrollTo = layout;
                    }
                }
                else
                {
                    CGui.OpenedGroup.Add(group);
                    CurrentLayout = layout;
                    CurrentElement = null;
                }
            }
            if (col)
            {
                ImGui.PopStyleColor();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            {
                layout.Enabled = !layout.Enabled;
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup("LayoutContext");
            }
            Safe(delegate
            {
                if (ImGui.BeginDragDropSource())
                {
                    ImGuiDragDrop.SetDragDropPayload("MoveLayout", index);
                    ImGuiEx.Text($"Moving layout\n??".Loc(layout.GetName()));
                    ImGui.EndDragDropSource();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    if (ImGuiDragDrop.AcceptDragDropPayload("MoveLayout", out int indexOfMovedObj, 
                        ImGuiDragDropFlags.AcceptNoDrawDefaultRect | ImGuiDragDropFlags.AcceptBeforeDelivery))
                    {
                        ImGuiUtils.DrawLine(curpos, contRegion);
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            var exch = P.Config.LayoutsL[indexOfMovedObj];
                            exch.Group = group ?? "";
                            P.Config.LayoutsL[indexOfMovedObj] = null;
                            P.Config.LayoutsL.Insert(index, exch);
                            P.Config.LayoutsL.RemoveAll(x => x == null);
                        }
                    }
                    ImGui.EndDragDropTarget();
                }
            });
            if (ImGui.BeginPopup("LayoutContext"))
            {
                ImGuiEx.Text($"Layout ??".Loc(layout.GetName()));
                if (ImGui.Selectable("Archive layout".Loc()))
                {
                    P.Archive.LayoutsL.Add(layout.JSONClone());
                    P.SaveArchive();
                    layout.Delete = true;
                }
                ImGui.Separator();
                if (ImGui.Selectable("Delete layout".Loc()))
                {
                    layout.Delete = true;
                }
                ImGui.EndPopup();
            }
        }
        if (CurrentLayout == layout)
        {
            for (var i = 0;i<CurrentLayout.ElementsL.Count;i++)
            {
                var e = CurrentLayout.ElementsL[i];
                ImGui.PushID(e.GUID);
                ImGui.SetCursorPosX(group == null? 10 : 20);
                var col = false;
                if (!e.Enabled)
                {
                    col = true;
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3);
                }
                else if (!layout.Enabled)
                {
                    col = true;
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                }
                var curpos = ImGui.GetCursorScreenPos();
                var contRegion = ImGui.GetContentRegionAvail().X;
                if (ImGui.Selectable($"{e.GetName()}", CurrentElement == e))
                {
                    if (CurrentElement == e)
                    {
                        CurrentElement = null;
                    }
                    else
                    {
                        CGui.OpenedGroup.Add(group);
                        CurrentElement = e;
                    }
                }
                if (col)
                {
                    ImGui.PopStyleColor();
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
                {
                    e.Enabled = !e.Enabled;
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ImGui.OpenPopup("ElementContext");
                }
                if (ImGui.BeginDragDropSource())
                {
                    ImGuiDragDrop.SetDragDropPayload($"MoveElement{index}", i);
                    ImGuiEx.Text($"Moving element\n??".Loc(layout.GetName()));
                    ImGui.EndDragDropSource();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    if (ImGuiDragDrop.AcceptDragDropPayload($"MoveElement{index}", out int indexOfMovedObj,
                        ImGuiDragDropFlags.AcceptNoDrawDefaultRect | ImGuiDragDropFlags.AcceptBeforeDelivery))
                    {
                        ImGuiUtils.DrawLine(curpos, contRegion);
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            var exch = CurrentLayout.ElementsL[indexOfMovedObj];
                            CurrentLayout.ElementsL[indexOfMovedObj] = null;
                            CurrentLayout.ElementsL.Insert(i, exch);
                            CurrentLayout.ElementsL.RemoveAll(x => x == null);
                        }
                    }
                    ImGui.EndDragDropTarget();
                }
                if (ImGui.BeginPopup("ElementContext"))
                {
                    ImGuiEx.Text($"{"Layout".Loc()} {layout.GetName()}\n{"Element".Loc()} {e.GetName()}");
                    if (ImGui.Selectable("Delete element".Loc()))
                    {
                        e.Delete = true;
                    }
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            ImGuiEx.LineCentered("AddElement", delegate
            {
                if(ImGui.SmallButton("Add element".Loc()))
                {
                    layout.ElementsL.Add(new(0));
                }
                ImGui.SameLine(); 
                if (ImGui.SmallButton("Paste".Loc()))
                {
                    try
                    {
                        layout.ElementsL.Add(JsonConvert.DeserializeObject<Element>(ImGui.GetClipboardText()));
                    }
                    catch(Exception e)
                    {
                        Notify.Error($"{e.Message}");
                    }
                }
            });
        }
        ImGui.PopID();
    }
}
