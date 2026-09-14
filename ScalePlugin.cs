using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ScalePlugin.ScaleCommands))]

namespace ScalePlugin
{
    public class ScaleCommands
    {
        [CommandMethod("SCALEMANAGER", CommandFlags.UsePickSet)]
        public void RunScaleManager()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            // Снимаем Pickfirst, пока команда ещё активна.
            ObjectId[] preselected = null;
            PromptSelectionResult sel = ed.SelectImplied();
            if (sel.Status == PromptStatus.OK && sel.Value != null && sel.Value.Count > 0)
                preselected = sel.Value.GetObjectIds();

            // Сразу снимаем подсветку, чтобы не мешала пользователю.
            ed.SetImpliedSelection(new ObjectId[0]);

            MenuScaleForm form = new MenuScaleForm(preselected);
            AcAp.ShowModelessDialog(form);
        }
    }
}