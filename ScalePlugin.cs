using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ScalePlugin.ScaleCommands))]

namespace ScalePlugin
{
    public class ScaleCommands
    {
        [CommandMethod("SCALEMANAGER")]
        public void RunScaleManager()
        {
            MenuScaleForm form = new MenuScaleForm();
            AcAp.ShowModelessDialog(form);
        }
    }
}