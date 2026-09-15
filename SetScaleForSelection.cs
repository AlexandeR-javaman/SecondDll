using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ScalePlugin
{
    public static class SetScaleForSelection
    {
        /// <summary>
        /// Разбор строки вида "1:100" → 100.0
        /// "2:1" → 0.5
        /// </summary>
        public static double ParseScaleFactor(string scaleText)
        {
            string[] parts = scaleText.Split(':');

            if (parts.Length != 2)
                return 1.0;

            double denom = double.Parse(
                parts[0].Replace('.', ','),
                CultureInfo.InvariantCulture);

            double num = double.Parse(
                parts[1].Replace('.', ','),
                CultureInfo.InvariantCulture);

            return num / denom;
        }

        /// <summary>
        /// Выбор блока и получение имени его слоя.
        /// </summary>
        public static string PickBlockLayer()
        {
            Document doc =
                AcAp.DocumentManager.MdiActiveDocument;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions opt =
                new PromptEntityOptions(
                    "\nВыберите блок на слое оформления: ");

            opt.SetRejectMessage(
                "\nЭто не блок. Попробуйте снова.");

            opt.AddAllowedClass(
                typeof(BlockReference),
                true);

            PromptEntityResult res =
                ed.GetEntity(opt);

            if (res.Status != PromptStatus.OK)
                return null;

            using (Transaction tr =
                   db.TransactionManager.StartTransaction())
            {
                BlockReference br =
                    tr.GetObject(
                        res.ObjectId,
                        OpenMode.ForRead) as BlockReference;

                if (br == null)
                {
                    ed.WriteMessage("\nЭто не блок.");
                    return null;
                }

                string layer = br.Layer;

                ed.WriteMessage(
                    $"\nВыбранный блок находится на слое: {layer}");

                return layer;
            }
        }

        /// <summary>
        /// Основная операция изменения масштаба.
        ///
        /// Dimension:
        ///     .NET API + Transaction
        ///
        /// MLeader:
        ///     .NET API + Transaction
        ///
        /// BlockReference:
        ///     COM API через AcadObject
        ///
        /// scaleFactor является АБСОЛЮТНЫМ масштабом.
        /// Например:
        ///     100  → X=100, Y=100, Z=100
        ///     0.5  → X=0.5, Y=0.5, Z=0.5
        /// </summary>
        public static void Run(
            double scaleFactor,
            string blockLayer,
            bool applyToDimensions,
            bool applyToMLeaders,
            bool applyToBlocks,
            ObjectId[] preselectedIds = null)
        {
            Document doc =
                AcAp.DocumentManager.MdiActiveDocument;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (string.IsNullOrEmpty(blockLayer))
                blockLayer = "1ЭП_Оформление";

            // =====================================================
            // 1. Получаем набор объектов
            // =====================================================

            ObjectId[] ids = preselectedIds;

            if (ids == null || ids.Length == 0)
            {
                PromptSelectionResult sel =
                    ed.SelectImplied();

                if (sel.Status == PromptStatus.OK &&
                    sel.Value != null &&
                    sel.Value.Count > 0)
                {
                    ids = sel.Value.GetObjectIds();
                }
                else
                {
                    PromptSelectionOptions pso =
                        new PromptSelectionOptions();

                    pso.MessageForAdding =
                        "\nВыберите объекты для обработки: ";

                    sel = ed.GetSelection(pso);

                    if (sel.Status != PromptStatus.OK ||
                        sel.Value == null ||
                        sel.Value.Count == 0)
                    {
                        ed.WriteMessage(
                            "\nНет выбранных объектов.");

                        return;
                    }

                    ids = sel.Value.GetObjectIds();
                }
            }

            int count = 0;

            // =====================================================
            // 2. Блокируем документ
            // =====================================================

            using (doc.LockDocument())
            {
                // =================================================
                // 3. Обработка Dimension и MLeader через .NET
                //
                // BlockReference здесь НЕ изменяем.
                // =================================================

                if (applyToDimensions || applyToMLeaders)
                {
                    using (Transaction tr =
                           db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in ids)
                        {
                            if (id.IsNull || id.IsErased)
                                continue;

                            Entity ent =
                                tr.GetObject(
                                    id,
                                    OpenMode.ForWrite) as Entity;

                            if (ent == null)
                                continue;

                            // -----------------------------------------
                            // Размер
                            // -----------------------------------------

                            if (ent is Dimension dim)
                            {
                                if (!applyToDimensions)
                                    continue;

                                dim.Dimscale = scaleFactor;

                                count++;

                                continue;
                            }

                            // -----------------------------------------
                            // Мультивыноска
                            // -----------------------------------------

                            if (ent is MLeader mld)
                            {
                                if (!applyToMLeaders)
                                    continue;

                                mld.Scale = scaleFactor;

                                count++;

                                continue;
                            }
                        }

                        tr.Commit();
                    }
                }

                // =================================================
                // 4. Получаем BlockReference через .NET
                //
                // Здесь мы НЕ изменяем блок.
                //
                // Нам нужно только получить:
                //     ObjectId
                //     Layer
                //
                // и передать сам BlockReference дальше,
                // чтобы получить его AcadObject.
                // =================================================

                List<BlockReference> blocks =
                    new List<BlockReference>();

                if (applyToBlocks)
                {
                    using (Transaction tr =
                           db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in ids)
                        {
                            if (id.IsNull || id.IsErased)
                                continue;

                            BlockReference br =
                                tr.GetObject(
                                    id,
                                    OpenMode.ForRead) as BlockReference;

                            if (br == null)
                                continue;

                            if (!string.Equals(
                                    br.Layer,
                                    blockLayer,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            blocks.Add(br);
                        }

                        tr.Commit();
                    }
                }

                // =================================================
                // 5. Блоки — COM API
                //
                // Здесь уже нет активной .NET Transaction.
                //
                // Используем:
                //
                //     br.AcadObject
                //
                // вместо:
                //
                //     ObjectIDToObject
                //
                // поэтому OldId / OldIdPtr вообще не нужны.
                // =================================================

                if (applyToBlocks)
                {
                    foreach (BlockReference br in blocks)
                    {
                        object comBr = br.AcadObject;

                        if (comBr == null)
                            continue;

                        Type comType =
                            comBr.GetType();

                        // -----------------------------------------
                        // Абсолютный масштаб блока.
                        //
                        // Аналог VBA:
                        //
                        // entity.XScaleFactor = myScaleFactor
                        // entity.YScaleFactor = myScaleFactor
                        // entity.ZScaleFactor = myScaleFactor
                        // -----------------------------------------

                        comType.InvokeMember(
                            "XScaleFactor",
                            BindingFlags.SetProperty,
                            null,
                            comBr,
                            new object[]
                            {
                                scaleFactor
                            });

                        comType.InvokeMember(
                            "YScaleFactor",
                            BindingFlags.SetProperty,
                            null,
                            comBr,
                            new object[]
                            {
                                scaleFactor
                            });

                        comType.InvokeMember(
                            "ZScaleFactor",
                            BindingFlags.SetProperty,
                            null,
                            comBr,
                            new object[]
                            {
                                scaleFactor
                            });

                        count++;
                    }
                }
            }

            // =====================================================
            // 6. Завершение
            // =====================================================

            ed.SetImpliedSelection(
                new ObjectId[0]);

            ed.WriteMessage(
                $"\nОбработано объектов: {count}");

            ed.Regen();
        }
    }
}
