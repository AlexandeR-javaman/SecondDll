using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry; // .NET API, в дальнейшем поменял на COM API для корректного изменения масштаба блоков с атрибутами
using System;
using System.Globalization;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ScalePlugin
{
    public static class SetScaleForSelection
    {
        /// <summary>
        /// Разбор строки вида "1:100" → 100.0 ; "2:1" → 0.5.
        /// </summary>
        public static double ParseScaleFactor(string scaleText)
        {
            string[] parts = scaleText.Split(':');
            if (parts.Length != 2) return 1.0;

            double denom = double.Parse(parts[0].Replace('.', ','), CultureInfo.InvariantCulture);
            double num = double.Parse(parts[1].Replace('.', ','), CultureInfo.InvariantCulture);

            return num / denom;
        }

        /// <summary>
        /// Выбор блока и получение имени его слоя.
        /// </summary>
        public static string PickBlockLayer()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions opt = new PromptEntityOptions("\nВыберите блок на слое оформления: ");
            opt.SetRejectMessage("\nЭто не блок. Попробуйте снова.");
            opt.AddAllowedClass(typeof(BlockReference), true);

            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference br = tr.GetObject(res.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null)
                {
                    ed.WriteMessage("\nЭто не блок.");
                    return null;
                }
                string layer = br.Layer;
                ed.WriteMessage($"\nВыбранный блок находится на слое: {layer}");
                return layer;
            }
        }

        /// <summary>
        /// Основная операция: применить масштабный коэффициент к выбранным объектам.
        /// </summary>
        /// <param name="scaleFactor">Масштабный коэффициент.</param>
        /// <param name="blockLayer">Слой, на котором должны находиться блоки.</param>
        /// <param name="applyToDimensions">Обрабатывать размеры (Dimscale).</param>
        /// <param name="applyToMLeaders">Обрабатывать мультивыноски (Scale).</param>
        /// <param name="applyToBlocks">Обрабатывать блоки на заданном слое.</param>
        /// <param name="preselectedIds">
        /// Объекты, заранее выбранные пользователем до открытия формы.
        /// Если null или пусто — используется Pickfirst / интерактивный выбор.
        /// </param>
        public static void Run(
            double scaleFactor,
            string blockLayer,
            bool applyToDimensions,
            bool applyToMLeaders,
            bool applyToBlocks,
            ObjectId[] preselectedIds = null)
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (string.IsNullOrEmpty(blockLayer))
                blockLayer = "1ЭП_Оформление";

            // 1. Получаем набор объектов:
            //    а) заранее переданный из формы (Pickfirst, зафиксированный до показа формы);
            //    б) текущий Pickfirst;
            //    в) интерактивный выбор.
            ObjectId[] ids = preselectedIds;

            if (ids == null || ids.Length == 0)
            {
                PromptSelectionResult sel = ed.SelectImplied();
                if (sel.Status == PromptStatus.OK && sel.Value != null && sel.Value.Count > 0)
                {
                    ids = sel.Value.GetObjectIds();
                }
                else
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\nВыберите объекты для обработки: ";
                    sel = ed.GetSelection(pso);

                    if (sel.Status != PromptStatus.OK || sel.Value == null || sel.Value.Count == 0)
                    {
                        ed.WriteMessage("\nНет выбранных объектов.");
                        return;
                    }
                    ids = sel.Value.GetObjectIds();
                }
            }

            int count = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased) continue;

                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    if (ent is Dimension dim)
                    {
                        if (!applyToDimensions) continue;
                        dim.Dimscale = scaleFactor;
                        count++;
                    }
                    else if (ent is MLeader mld)
                    {
                        if (!applyToMLeaders) continue;
                        mld.Scale = scaleFactor;
                        count++;
                    }
// =============================================================
// БЛОКИ — здесь используется COM API AutoCAD.
//
// В .NET API изменение:
//     br.ScaleFactors = new Scale3d(...)
// корректно изменяет графическую часть блока, но для
// AttributeReference результат отличается от изменения
// масштаба блока вручную через Properties.
//
// В старой рабочей версии VBA использовались:
//     XScaleFactor
//     YScaleFactor
//     ZScaleFactor
// через AutoCAD COM/ActiveX API.
//
// COM-вариант корректно обрабатывает блок вместе с его
// атрибутами, поэтому для BlockReference намеренно
// используется COM, а не .NET ScaleFactors.
//
// scaleFactor здесь является АБСОЛЮТНЫМ масштабом блока,
// а не коэффициентом умножения.
// =============================================================
                    else if (ent is BlockReference br)
                    {
                        if (!applyToBlocks)
                            continue;

                        if (string.Equals(
                                br.Layer,
                                blockLayer,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            object comBr = br.AcadObject;

                            comBr.GetType().InvokeMember(
                                "XScaleFactor",
                                System.Reflection.BindingFlags.SetProperty,
                                null,
                                comBr,
                                new object[] { scaleFactor });

                            comBr.GetType().InvokeMember(
                                "YScaleFactor",
                                System.Reflection.BindingFlags.SetProperty,
                                null,
                                comBr,
                                new object[] { scaleFactor });

                            comBr.GetType().InvokeMember(
                                "ZScaleFactor",
                                System.Reflection.BindingFlags.SetProperty,
                                null,
                                comBr,
                                new object[] { scaleFactor });

                            br.RecordGraphicsModified(true);

                            count++;
                        }
                    }
                }

                tr.Commit();
            }

            // Снимаем Pickfirst (уже неактуален).
            ed.SetImpliedSelection(new ObjectId[0]);
            ed.WriteMessage($"\nОбработано объектов: {count}");
            ed.Regen();
        }
    }
}