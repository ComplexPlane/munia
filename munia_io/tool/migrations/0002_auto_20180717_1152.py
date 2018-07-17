# Generated by Django 2.0.7 on 2018-07-17 09:52

from django.db import migrations, models
import django.db.models.deletion


class Migration(migrations.Migration):

    dependencies = [
        ('tool', '0001_initial'),
    ]

    operations = [
        migrations.CreateModel(
            name='FirmwareVersion',
            fields=[
                ('id', models.AutoField(auto_created=True, primary_key=True, serialize=False, verbose_name='ID')),
                ('version', models.CharField(max_length=100, unique=True)),
                ('release_date', models.DateField(auto_now_add=True)),
                ('release_notes', models.CharField(max_length=100)),
                ('file', models.FileField(upload_to='versions')),
                ('supported_hw_revisions', models.CharField(max_length=100)),
                ('supported_device_ids', models.CharField(max_length=100)),
            ],
        ),
        migrations.CreateModel(
            name='InputAdapterDevice',
            fields=[
                ('id', models.AutoField(auto_created=True, primary_key=True, serialize=False, verbose_name='ID')),
                ('name', models.CharField(max_length=100, unique=True)),
            ],
        ),
        migrations.AlterField(
            model_name='programversion',
            name='file',
            field=models.FileField(upload_to='versions'),
        ),
        migrations.AlterField(
            model_name='programversion',
            name='release_notes',
            field=models.CharField(max_length=100),
        ),
        migrations.AddField(
            model_name='firmwareversion',
            name='device',
            field=models.ForeignKey(on_delete=django.db.models.deletion.CASCADE, to='tool.InputAdapterDevice'),
        ),
    ]