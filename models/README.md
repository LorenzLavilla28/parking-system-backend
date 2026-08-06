# License plate detector

`LicensePlateDetector_YOLOv8n.onnx` is the ONNX export from
[Murd0ck/LicensePlateDetector_YOLOv8n](https://huggingface.co/Murd0ck/LicensePlateDetector_YOLOv8n).
The model card identifies the model as CC BY 4.0 and notes that it was trained
primarily on Ukrainian plates, so it should be replaced or fine-tuned with a
Philippine-plate dataset before production rollout.

The backend uses this model only to localize the plate. The guard still confirms
the OCR result before recording an entry.

## PaddleOCR

`PaddleOCR/PP-OCRv5_mobile_det_infer` and
`PaddleOCR/en_PP-OCRv5_mobile_rec` are the official local PP-OCRv5 mobile
detection and English recognition models. They are bundled with the API so
production scanning does not download models or call a paid OCR service.

Model source: [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) (Apache-2.0).
