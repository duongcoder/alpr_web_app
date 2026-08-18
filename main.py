import os
from fastapi import FastAPI, Request, File, UploadFile, HTTPException
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from ai_model import predict_license_plate

from fastapi.concurrency import run_in_threadpool

app = FastAPI(
    title="Vietnamese ALPR Web App",
    description="Automatic License Plate Recognition Web Application using FastAPI & Modern UI",
    version="1.0.0"
)

# Ensure static and templates directories exist
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
STATIC_DIR = os.path.join(BASE_DIR, "static")
TEMPLATES_DIR = os.path.join(BASE_DIR, "templates")

os.makedirs(STATIC_DIR, exist_ok=True)
os.makedirs(TEMPLATES_DIR, exist_ok=True)

# Mount static files (CSS, JS, assets)
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")

# Templates setup
templates = Jinja2Templates(directory=TEMPLATES_DIR)


@app.get("/", response_class=HTMLResponse)
async def read_index(request: Request):
    """
    Renders the main ALPR web application interface.
    """
    return templates.TemplateResponse(request=request, name="index.html")


@app.post("/api/detect")
async def detect_license_plate(file: UploadFile = File(...)):
    """
    Endpoint for uploading image and getting ALPR detection result.
    """
    # Basic validation for image file
    if not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="File tải lên phải là hình ảnh (jpg, png, webp,...).")

    try:
        contents = await file.read()
        if not contents:
            raise HTTPException(status_code=400, detail="File ảnh rỗng.")
            
        # Call AI model prediction
        result = await run_in_threadpool(predict_license_plate, contents, file.filename)
        return JSONResponse(content=result)
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi xử lý hình ảnh: {str(e)}")
    finally:
        await file.close()


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="127.0.0.1", port=8000, reload=True)
