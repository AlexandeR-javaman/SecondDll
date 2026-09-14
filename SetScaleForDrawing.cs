using Autodesk.AutoCAD.ApplicationServices;
using System;
using System.Threading.Tasks;

namespace ScalePlugin
{
    public static class SetScaleForDrawing
    {
        public static async Task RunAsync(double scaleFactor)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // 1. Валидация: масштаб не может быть нулевым или отрицательным
            if (scaleFactor <= 0)
            {
                doc.Editor.WriteMessage("\nОшибка: Масштабный коэффициент должен быть строго больше нуля.\n");
                return;
            }

            try
            {
                // Переключаемся в контекст команды документа
                await Application.DocumentManager.ExecuteInCommandContextAsync(
                    async (obj) =>
                    {
                        // Теперь мы в командном контексте — можно безопасно
                        // менять системные переменные без «залипания»
                        Application.SetSystemVariable("DIMSCALE", scaleFactor);
                        Application.SetSystemVariable("MLEADERSCALE", scaleFactor);
                        
                        await Task.CompletedTask;
                    }, null);

                doc.Editor.WriteMessage($"\nУспешно установлены: DIMSCALE = {scaleFactor}, MLEADERSCALE = {scaleFactor}\n");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception acadEx)
            {
                // Ловим специфичные исключения AutoCAD (например, eInvalidInput)
                doc.Editor.WriteMessage($"\nОшибка AutoCAD при установке переменных: {acadEx.Message} (Код: {acadEx.ErrorStatus})\n");
            }
            catch (Exception ex)
            {
                // Ловим любые другие непредвиденные ошибки
                doc.Editor.WriteMessage($"\nНепредвиденная ошибка при установке системных переменных: {ex.Message}\n");
                
                // Совет: здесь также полезно записать ex.ToString() в лог-файл для отладки
            }
        }
    }
}