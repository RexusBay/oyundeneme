# Render ile yayinlama

Bu proje Docker ile deploy'a hazirdir.

## 1. Kodu GitHub'a yukle

Bu klasoru bir GitHub reposuna push et.

## 2. Render'da yeni servis ac

1. Render hesabina gir.
2. `New +` > `Blueprint` sec.
3. GitHub reposunu bagla.
4. Render, kok klasordeki `render.yaml` dosyasini okuyup web servisini olustursun.

## 3. Canli linki al

Deploy bitince sana su formatta bir link verir:

`https://ask-oyunu.onrender.com`

Bu linki kiz arkadasinla direkt paylasabilirsin.

## 4. Oyun kullanimi

1. Biriniz odayi olusturur.
2. `Linki kopyala` butonuna basar.
3. Digeriniz linkten girer.
4. Ayni odada aninda oynarsiniz.

## Not

- WebSocket kullandigi icin statik site degil, `Web Service` olarak deploy edilmelidir.
- Ilk acilista platform uyku modundaysa birkac saniye gecikme olabilir.
